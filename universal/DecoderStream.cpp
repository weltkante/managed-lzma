#include "pch.h"
#include "DecoderStream.h"
#include "DecoderSettings.h"
#include "Utilities.h"

using namespace ManagedLzma::Universal;
using namespace Platform;

DecoderStream::DecoderStream(Windows::Storage::Streams::IInputStream^ input, DecoderSettings^ settings)
	: mInput(input)
	, mSettings(settings)
{
	LzmaDec_Construct(&mDecoder);
	byte props[LZMA_PROPS_SIZE];
	settings->WriteTo(props);
	auto res = LzmaDec_Allocate(&mDecoder, props, LZMA_PROPS_SIZE, kAllocImpl);
	if (res != SZ_OK)
		throw CreateException(res);
}

DecoderStream::~DecoderStream()
{
	LzmaDec_Free(&mDecoder, kAllocImpl);
}

Windows::Foundation::IAsyncOperationWithProgress<Windows::Storage::Streams::IBuffer^, uint32_t>^ DecoderStream::ReadAsync(
	Windows::Storage::Streams::IBuffer^ pBuffer, uint32_t count, Windows::Storage::Streams::InputStreamOptions options)
{
	HRESULT hr = S_OK;

	auto inputLength = pBuffer->Length;

	Microsoft::WRL::ComPtr<Windows::Storage::Streams::IBufferByteAccess> pBufferAccess;
	hr = reinterpret_cast<IUnknown*>(pBuffer)->QueryInterface(IID_PPV_ARGS(&pBufferAccess));
	if (FAILED(hr))
		throw Exception::CreateException(hr);
	
	LPBYTE pBufferContent = NULL;;
	hr = pBufferAccess->Buffer(&pBufferContent);
	if (FAILED(hr))
		throw Exception::CreateException(hr);

	size_t decoderOutputLength = inputLength;
	size_t decoderInputLength = 0u;
	ELzmaStatus status;

	SRes res = LzmaDec_DecodeToBuf(&mDecoder, pBufferContent, &decoderOutputLength, nullptr, &decoderInputLength, ELzmaFinishMode::LZMA_FINISH_ANY, &status);
	if (res != SZ_OK)
		throw CreateException(res);

	switch (status)
	{
	case LZMA_STATUS_NOT_FINISHED:
	case LZMA_STATUS_NEEDS_MORE_INPUT:
	case LZMA_STATUS_FINISHED_WITH_MARK:
	case LZMA_STATUS_MAYBE_FINISHED_WITHOUT_MARK:
		break;

	default:
		throw ref new FailureException(L"DecoderStream has been corrupted.");
	}

	throw Exception::CreateException(E_NOTIMPL);
}
