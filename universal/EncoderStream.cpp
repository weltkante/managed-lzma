#include "pch.h"
#include "EncoderStream.h"
#include "Utilities.h"
#include <windows.storage.streams.h>

#include "Native/LzmaEnc.h"

using namespace Platform;
using namespace Microsoft::WRL;
using namespace Windows::Foundation;
using namespace Windows::Storage::Streams;
using namespace ManagedLzma::Universal;

class NativeBuffer : public RuntimeClass<RuntimeClassFlags<RuntimeClassType::WinRtClassicComMix>, ABI::Windows::Storage::Streams::IBuffer, IBufferByteAccess>
{
public:
	virtual ~NativeBuffer()
	{
		if (m_delete)
			free(m_buffer);
	}

	STDMETHODIMP RuntimeClassInitialize(void* buffer, size_t totalSize)
	{
		m_length = totalSize;
		m_buffer = buffer;
		return S_OK;
	}

	void Persist()
	{
		// TODO: locking
		auto buffer = malloc(m_length);
		memcpy(buffer, m_buffer, m_length);
		m_buffer = buffer;
	}

	STDMETHODIMP Buffer(byte** value)
	{
		if (!value) return E_POINTER;
		*value = static_cast<byte*>(m_buffer);
		return S_OK;
	}

	STDMETHODIMP get_Capacity(UINT32* value)
	{
		if (!value) return E_POINTER;
		*value = m_length;
		return S_OK;
	}

	STDMETHODIMP get_Length(UINT32* value)
	{
		if (!value) return E_POINTER;
		*value = m_length;
		return S_OK;
	}

	STDMETHODIMP put_Length(UINT32 value)
	{
		m_length = value;
		return S_OK;
	}

private:
	UINT32 m_length;
	void* m_buffer;
	bool m_delete;
};

struct HelperContainer
{
	HelperContainer();

	EncoderStream^ pEncoder;
	ISeqInStream mInputHelper;
	ISeqOutStream mOutputHelper;
	ICompressProgress mProgressHelper;
};

static SRes ReadStreamImpl(void* p, void* buf, size_t* size)
{
	auto container = reinterpret_cast<HelperContainer*>(static_cast<byte*>(p) - offsetof(HelperContainer, mInputHelper));
	return container->pEncoder->ReadStream(buf, size);
}

static size_t WriteStreamImpl(void* p, const void* buf, size_t size)
{
	auto container = reinterpret_cast<HelperContainer*>(static_cast<byte*>(p) - offsetof(HelperContainer, mOutputHelper));
	return container->pEncoder->WriteStream(buf, size);
}

static SRes ProgressReportImpl(void* p, UInt64 inSize, UInt64 outSize)
{
	auto container = reinterpret_cast<HelperContainer*>(static_cast<byte*>(p) - offsetof(HelperContainer, mProgressHelper));
	return container->pEncoder->ReportProgress(inSize, outSize);
}

HelperContainer::HelperContainer()
	: pEncoder(nullptr)
	, mInputHelper({ &ReadStreamImpl })
	, mOutputHelper({ &WriteStreamImpl })
	, mProgressHelper({ &ProgressReportImpl })
{
}

EncoderStream::EncoderStream(IOutputStream^ output)
	: mInputOffset(0)
	, mOutputOffset(0)
	, mOutput(output)
	, mEncoderTask(concurrency::task_from_result())
{
	mEncoder = LzmaEnc_Create(kAllocImpl);
}

EncoderStream::~EncoderStream()
{
	LzmaEnc_Destroy(mEncoder, kAllocImpl, kAllocImpl);
}

// if (input(*size) != 0 && output(*size) == 0) means end_of_stream.
//    (output(*size) < input(*size)) is allowed
SRes EncoderStream::ReadStream(void* buf, size_t* size)
{
	if (!buf || !size) return SZ_ERROR_PARAM;

	while (mRequestQueue.empty()) {
		if (mFlush) {
			*size = 0;
			return SZ_OK;
		}

		// wait for incoming write requests because we aren't allowed to return zero bytes
	}

	auto request = mRequestQueue.front();

	// changing the buffer size during the WriteAsync call is not allowed
	if (request->mBufferLength != request->pBuffer->Length) {
		*size = 0;
		return SZ_ERROR_FAIL;
	}

	*size = std::min(*size, request->mBufferLength - request->mBufferOffset);

	// I don't know how long the backing pointer returned here is valid, so we request it again each time
	HRESULT hr;
	byte* pBufferContent;
	if (FAILED(hr = request->pBufferAccess->Buffer(&pBufferContent))) {
		*size = 0;
		return SZ_ERROR_FAIL;
	}

	// Copy the memory over to the encoder
	memcpy(buf, pBufferContent + request->mBufferOffset, *size);
	request->mBufferOffset += *size;

	// If the buffer has been completely copied we can complete the WriteAsync request, otherwise we report progress.
	if (request->mBufferOffset == request->mBufferLength)
		request->mCompletion.set(request->mBufferLength);
	else
		request->mProgress.report((100u * request->mBufferOffset) / request->mBufferLength);

	return SZ_OK;
}

// Returns: result - the number of actually written bytes.
//          (result < size) means error
size_t EncoderStream::WriteStream(const void* buf, size_t size)
{
	// TODO: change this so we have an IInputStream we provide on the Decoder which can then be copied over to an IOutputStream
	//       this allows the caller to determine how large the copy-buffer should be, instead of us having to copy all incoming data

	if (!buf || !size) return 0;
	HRESULT hr;
	ComPtr<ABI::Windows::Storage::Streams::IBuffer> pBuffer;
	hr = MakeAndInitialize<NativeBuffer>(&pBuffer, const_cast<void*>(buf), size);
	if (FAILED(hr)) return 0;
	auto result = mOutput->WriteAsync(reinterpret_cast<IBuffer^>(pBuffer.Get()));
	if (result->Status != Windows::Foundation::AsyncStatus::Completed)
		static_cast<NativeBuffer*>(pBuffer.Get())->Persist();
	return size;
}

SRes EncoderStream::ReportProgress(uint64_t inSize, uint64_t outSize)
{
	auto& request = mRequestQueue.front();

	// inputs may be -1 to indicate that the values are unknown

	if (inSize != ~0ull)
	{
		auto offset = inSize - request->mProgressOffset;
		auto length = request->pBuffer->Length;
		request->mProgress.report(static_cast<uint32_t>((100u * offset) / length));
	}

	if (outSize != ~0ull)
		mOutputOffset = outSize;

	// return something else than SZ_OK to request cancellation
	return request->mCancellation.is_canceled() ? -1 : SZ_OK;
}

void EncoderStream::StartEncoding()
{
	mFlush = false;
	mEncoderTask = concurrency::create_task([this] {
		auto res = LzmaEnc_Encode(mEncoder, &mHelper->mOutputHelper, &mHelper->mInputHelper, &mHelper->mProgressHelper, kAllocImpl, kAllocImpl);
		if (res != SZ_OK)
			throw CreateException(res);
	});
}

IAsyncOperation<bool>^ EncoderStream::FlushAsync()
{
	return concurrency::create_async([this] {
		mFlush = true;
		return mEncoderTask.then([] {
			return false;
		});
	});
}

IAsyncOperationWithProgress<uint32_t, uint32_t>^ EncoderStream::WriteAsync(IBuffer^ pBuffer)
{
	return concurrency::create_async([this, pBuffer](concurrency::progress_reporter<uint32_t> progress, concurrency::cancellation_token ct) {
		HRESULT hr = S_OK;
		auto request = std::make_shared<WriteRequest>(mInputOffset, pBuffer, progress, ct, hr);
		if (FAILED(hr)) throw Exception::CreateException(hr);
		mInputOffset += request->mBufferLength;
		auto completion = request->mCompletion;
		mRequestQueue.push(std::move(request));
		if (mEncoderTask.is_done()) StartEncoding();
		return concurrency::create_task(completion);
	});
}
