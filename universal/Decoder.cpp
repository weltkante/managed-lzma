#include "pch.h"
#include "Decoder.h"
#include "DecoderSettings.h"
#include "Utilities.h"
#include <windows.storage.streams.h>

using namespace ManagedLzma::Universal;
using namespace Platform;

#pragma region Decoder

Decoder::Decoder(DecoderSettings^ settings)
	: mSettings(settings)
{
	if (!settings)
		throw ref new InvalidArgumentException();

	LzmaDec_Construct(&mDecoder);
	byte props[LZMA_PROPS_SIZE];
	settings->WriteTo(props);
	auto res = LzmaDec_Allocate(&mDecoder, props, LZMA_PROPS_SIZE, kAllocImpl);
	if (res != SZ_OK)
		throw CreateException(res);
}

Decoder::~Decoder()
{
	for (;;)
	{
		concurrency::critical_section::scoped_lock lock(mSync);

		if (!mRunning)
			break;

		// TODO: stop running decoder and wait until it completes
		RoFailFastWithErrorContext(E_NOTIMPL);
	}

	LzmaDec_Free(&mDecoder, kAllocImpl);
}

Windows::Foundation::IAsyncAction^ Decoder::CancelAsync()
{
	// TODO: we should cancel all outstanding tasks and then complete this action
	throw ref new NotImplementedException();
}

Windows::Foundation::IAsyncAction^ Decoder::CompleteInputAsync()
{
	concurrency::critical_section::scoped_lock lock(mSync);

	mFlushed = true;

	return concurrency::create_async([this] {
		RoFailFastWithErrorContext(E_NOTIMPL);
	});
}

Windows::Foundation::IAsyncOperationWithProgress<uint32_t, uint32_t>^ Decoder::ProvideInputAsync(Windows::Storage::Streams::IBuffer^ buffer)
{
	if (!buffer)
		throw ref new NullReferenceException(L"buffer cannot be null");

	return concurrency::create_async([&](concurrency::progress_reporter<uint32_t> progress) {
		auto pInputFrame = std::make_shared<InputFrame>();
		pInputFrame->mBuffer = buffer;
		pInputFrame->mOffset = 0;
		pInputFrame->mEnding = buffer->Length;
		pInputFrame->mProgress = progress;
		PushInputFrame(pInputFrame);
		return concurrency::create_task(pInputFrame->mCompletion);
	});
}

Windows::Foundation::IAsyncOperationWithProgress<Windows::Storage::Streams::IBuffer^, uint32_t>^ Decoder::ReadOutputAsync(
	Windows::Storage::Streams::IBuffer^ buffer, uint32_t length, Windows::Storage::Streams::InputStreamOptions options)
{
	if (!buffer)
		throw ref new NullReferenceException(L"buffer cannot be null");

	if (length > buffer->Capacity - buffer->Length)
		throw ref new InvalidArgumentException(L"buffer not large enough");

	return concurrency::create_async([&](concurrency::progress_reporter<uint32_t> progress) {
		auto pOutputFrame = std::make_shared<OutputFrame>();
		pOutputFrame->mBuffer = buffer;
		pOutputFrame->mOffset = buffer->Length;
		pOutputFrame->mEnding = pOutputFrame->mOffset + length;
		pOutputFrame->mProgress = progress;
		pOutputFrame->mOptions = options;
		PushOutputFrame(pOutputFrame);
		return concurrency::create_task(pOutputFrame->mCompletion);
	});
}

void Decoder::PushInputFrame(const std::shared_ptr<InputFrame>& frame)
{
	concurrency::critical_section::scoped_lock lock(mSync);
	mInputQueue.push(frame);
	TryStartOperation();
}

void Decoder::PushOutputFrame(const std::shared_ptr<OutputFrame>& frame)
{
	concurrency::critical_section::scoped_lock lock(mSync);
	mOutputQueue.push(frame);
	TryStartOperation();
}

void Decoder::TryStartOperation()
{
	// Caller locks.
	//concurrency::critical_section::scoped_lock lock(mSync);

	if (!mRunning)
	{
		if (!mStarted)
		{
			mStarted = true;
			LzmaDec_Init(&mDecoder);
		}

		mOperation = concurrency::create_task([this] {
			do { WriteOutput(); } while (DecodeInput());
		});
	}
}

void Decoder::WriteOutput()
{
	while (mDecoderPosition != mDecoder.dicPos)
	{
		OutputFrame* pOutputFrame;
		{
			// Access to the output queue must be locked.
			concurrency::critical_section::scoped_lock lock(mSync);

			if (!mRunning || !mStarted)
				RoFailFastWithErrorContext(E_UNEXPECTED);

			if (mOutputQueue.empty())
				break;

			pOutputFrame = mOutputQueue.front().get();
		}

		HRESULT hr = S_OK;
		Microsoft::WRL::ComPtr<Windows::Storage::Streams::IBufferByteAccess> pOutputBufferAccess;
		if (FAILED(hr = reinterpret_cast<IUnknown*>(pOutputFrame->mBuffer)->QueryInterface(IID_PPV_ARGS(&pOutputBufferAccess))))
			throw Exception::CreateException(hr);

		byte* pOutputBuffer = NULL;
		if (FAILED(hr = pOutputBufferAccess->Buffer(&pOutputBuffer)))
			throw Exception::CreateException(hr);

		auto capacity = pOutputFrame->mEnding - pOutputFrame->mOffset;
		auto copySize = std::min(capacity, mDecoder.dicPos - mDecoderPosition);
		memcpy(pOutputBuffer + pOutputFrame->mOffset, mDecoder.dic + mDecoderPosition, copySize);

		pOutputBufferAccess.Reset();
		pOutputFrame->mOffset += copySize;
		mDecoderPosition += copySize;

		if (copySize == capacity || (pOutputFrame->mOptions & Windows::Storage::Streams::InputStreamOptions::Partial) == Windows::Storage::Streams::InputStreamOptions::Partial)
		{
			pOutputFrame->mCompletion.set(pOutputFrame->mBuffer);

			// Access to the output queue must be locked.
			concurrency::critical_section::scoped_lock lock(mSync);

			if (!mRunning || !mStarted)
				RoFailFastWithErrorContext(E_UNEXPECTED);

			mOutputQueue.pop();
		}
		else
		{
			auto progress = (100.0f * pOutputFrame->mOffset) / pOutputFrame->mEnding;
			pOutputFrame->mProgress.report((uint32_t)lround(progress));
		}
	}
}

bool Decoder::DecodeInput()
{
	ELzmaFinishMode mode;
	InputFrame* pInputFrame;
	{
		// Access to the input queue must be locked.
		concurrency::critical_section::scoped_lock lock(mSync);

		if (!mRunning || !mStarted)
			RoFailFastWithErrorContext(E_UNEXPECTED);

		if (mInputQueue.empty())
		{
			mRunning = false;
			return false;
		}

		mode = mFlushed && mInputQueue.size() == 1 ? LZMA_FINISH_END : LZMA_FINISH_ANY;
		pInputFrame = mInputQueue.front().get();
	}

	HRESULT hr = S_OK;
	Microsoft::WRL::ComPtr<Windows::Storage::Streams::IBufferByteAccess> pInputBufferAccess;
	if (FAILED(hr = reinterpret_cast<IUnknown*>(pInputFrame->mBuffer)->QueryInterface(IID_PPV_ARGS(&pInputBufferAccess))))
		throw Exception::CreateException(hr);

	byte* pInputBuffer = NULL;
	if (FAILED(hr = pInputBufferAccess->Buffer(&pInputBuffer)))
		throw Exception::CreateException(hr);

	if (mDecoder.dicPos == mDecoder.dicBufSize) mDecoder.dicPos = 0;
	size_t dicLimit = std::min(mDecoderPosition + mTotalOutputCapacity, mDecoder.dicBufSize);
	size_t inputSize = pInputFrame->mEnding - pInputFrame->mOffset;
	ELzmaStatus status = LZMA_STATUS_NOT_SPECIFIED;

	SRes res = LzmaDec_DecodeToDic(&mDecoder, 0, pInputBuffer + pInputFrame->mOffset, &inputSize, LZMA_FINISH_ANY, &status);
	if (res != SZ_OK)
	{
		if (res != SZ_ERROR_DATA)
			RoFailFastWithErrorContext(E_UNEXPECTED);

		throw Exception::CreateException(HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
	}

	switch (status)
	{
	case LZMA_STATUS_FINISHED_WITH_MARK:
	case LZMA_STATUS_NOT_FINISHED:
	case LZMA_STATUS_NEEDS_MORE_INPUT:
	case LZMA_STATUS_MAYBE_FINISHED_WITHOUT_MARK:
		mDecoderState = status;
		break;

	default:
		RoFailFastWithErrorContext(E_UNEXPECTED);
	}

	pInputBufferAccess.Reset(); // we are done with the buffer for now
	pInputFrame->mOffset += inputSize;

	if (pInputFrame->mOffset == pInputFrame->mEnding)
	{
		pInputFrame->mCompletion.set(pInputFrame->mEnding);

		// Access to the input queue must be locked.
		concurrency::critical_section::scoped_lock lock(mSync);

		if (!mRunning || !mStarted)
			RoFailFastWithErrorContext(E_UNEXPECTED);

		mInputQueue.pop();
	}
	else
	{
		auto progress = (100.0f * pInputFrame->mOffset) / pInputFrame->mEnding;
		pInputFrame->mProgress.report((uint32_t)lround(progress));
	}

	return true;
}

#pragma endregion

#pragma region DecoderInputStream

DecoderInputStream::DecoderInputStream(Decoder^ decoder)
	: mDecoder(decoder)
{
}

DecoderInputStream::~DecoderInputStream()
{
	mDecoder = nullptr;
}

Windows::Foundation::IAsyncOperation<bool>^ DecoderInputStream::FlushAsync()
{
	return concurrency::create_async([this] { return concurrency::create_task(mDecoder->CompleteInputAsync()).then([] { return true; }); });
}

Windows::Foundation::IAsyncOperationWithProgress<uint32_t, uint32_t>^ DecoderInputStream::WriteAsync(Windows::Storage::Streams::IBuffer^ buffer)
{
	return mDecoder->ProvideInputAsync(buffer);
}

#pragma endregion

#pragma region DecoderInputStream

DecoderOutputStream::DecoderOutputStream(Decoder^ decoder)
	: mDecoder(decoder)
{
}

DecoderOutputStream::~DecoderOutputStream()
{
	mDecoder = nullptr;
}

Windows::Foundation::IAsyncOperationWithProgress<Windows::Storage::Streams::IBuffer^, uint32_t>^ DecoderOutputStream::ReadAsync(
	Windows::Storage::Streams::IBuffer^ buffer, uint32_t length, Windows::Storage::Streams::InputStreamOptions options)
{
	return mDecoder->ReadOutputAsync(buffer, length, options);
}

#pragma endregion
