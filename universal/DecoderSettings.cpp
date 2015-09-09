#include "pch.h"
#include "DecoderSettings.h"

using namespace ManagedLzma::Universal;
using namespace Platform;

const uint32_t kMinDictionarySize = 1 << 12; // LZMA_DIC_MIN in LzmaDec.c

static uint32_t DecodeDictionarySize(byte bt1, byte bt2, byte bt3, byte bt4)
{
	uint32_t size = bt1 | (bt2 << 8) | (bt3 << 16) | (bt4 << 24);
	return std::max(size, kMinDictionarySize);
}

static void DecodeFirstByte(byte input, byte& lc, byte& lp, byte& pb)
{
	if (input >= 9 * 5 * 5)
		throw ref new InvalidArgumentException();

	lc = input % 9;
	input /= 9;
	pb = input / 5;
	lp = input % 5;
}

DecoderSettings^ DecoderSettings::FromArray(const Array<byte>^ buffer)
{
	byte lc, lp, pb;
	DecodeFirstByte(buffer[0], lc, lp, pb);
	auto size = DecodeDictionarySize(buffer[1], buffer[2], buffer[3], buffer[4]);
	return ref new DecoderSettings(lc, lp, pb, size);
}

DecoderSettings^ DecoderSettings::FromBuffer(Windows::Storage::Streams::IBuffer^ pBuffer)
{
	if (pBuffer == nullptr || pBuffer->Length < 5)
		throw ref new InvalidArgumentException();

	HRESULT hr = S_OK;

	Microsoft::WRL::ComPtr<Windows::Storage::Streams::IBufferByteAccess> pBufferAccess;
	hr = reinterpret_cast<IUnknown*>(pBuffer)->QueryInterface(IID_PPV_ARGS(&pBufferAccess));
	if (FAILED(hr))
		throw Exception::CreateException(hr);

	LPBYTE pBufferContent = NULL;;
	hr = pBufferAccess->Buffer(&pBufferContent);
	if (FAILED(hr))
		throw Exception::CreateException(hr);

	byte lc, lp, pb;
	DecodeFirstByte(*pBufferContent, lc, lp, pb);
	auto size = DecodeDictionarySize(pBufferContent[1], pBufferContent[2], pBufferContent[3], pBufferContent[4]);
	return ref new DecoderSettings(lc, lp, pb, size);
}

DecoderSettings::DecoderSettings(byte lc, byte lp, byte pb, uint32_t dictionarySize)
	: mLC(lc)
	, mLP(lp)
	, mPB(pb)
	, mDictionarySize(dictionarySize)
{
	if (lc < 0 || lc > 8)
		throw ref new InvalidArgumentException();
	
	if (lp < 0 || lp > 4)
		throw ref new InvalidArgumentException();

	if (pb < 0 || pb > 4)
		throw ref new InvalidArgumentException();
}

Array<byte>^ DecoderSettings::ToArray()
{
	auto buffer = ref new Array<byte>(5);
	buffer[0] = ((mPB * 5 + mLP) * 9 + mLC);

	auto dictSize = mDictionarySize;
	for (int i = 11; i <= 30; ++i)
	{
		if (dictSize <= (2u << i)) { dictSize = (2u << i); break; }
		if (dictSize <= (3u << i)) { dictSize = (3u << i); break; }
	}

	for (int i = 0; i < 4; ++i)
		buffer[1 + i] = (dictSize >> (8 * i));

	return buffer;
}

void DecoderSettings::WriteTo(byte* p)
{
	p[0] = ((mPB * 5 + mLP) * 9 + mLC);

	auto dictSize = mDictionarySize;
	for (int i = 11; i <= 30; ++i)
	{
		if (dictSize <= (2u << i)) { dictSize = (2u << i); break; }
		if (dictSize <= (3u << i)) { dictSize = (3u << i); break; }
	}

	for (int i = 0; i < 4; ++i)
		p[1 + i] = (dictSize >> (8 * i));
}
