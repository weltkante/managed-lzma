#include "native.h"
#include "lzma/LzmaLib.h"
#include "lzma/Lzma2Enc.h"
#include "lzma/Lzma2Dec.h"

static void *SzAlloc(void *p, size_t size)
{
	if(size == 0) return 0;
	return malloc(size);
}
static void SzFree(void *p, void *address)
{
	free(address);
}
static ISzAlloc g_Alloc = { SzAlloc, SzFree };

struct OutContext
	: public ISeqOutStream
{
	unsigned char *pBuffer;
	size_t offset;
	const size_t length;

	OutContext(size_t (*Write)(void *p, const void *buf, size_t size), unsigned char *pBuffer, size_t length)
		: pBuffer(pBuffer), offset(0), length(length) { this->Write = Write; }
};

struct InContext
	: public ISeqInStream
{
	const unsigned char *pBuffer;
	size_t offset;
	const size_t length;

	InContext(SRes (*Read)(void *p, void *buf, size_t *size), const unsigned char *pBuffer, size_t length)
		: pBuffer(pBuffer), offset(0), length(length) { this->Read = Read; }
};

static size_t min_(size_t a, size_t b)
{
	if(a < b) return a; return b;
}

static size_t NativeLzmaCompress2_Write(void *p, const void *buf, size_t size)
{
	OutContext *c = static_cast<OutContext*>(p);
	size = min_(c->length - c->offset, size);
	if(size > 0)
	{
		memcpy(c->pBuffer + c->offset, buf, size);
		c->offset += size;
	}
	return size;
}

static SRes NativeLzmaCompress2_Read(void *p, void *buf, size_t *size)
{
	InContext *c = static_cast<InContext*>(p);
	*size = min_(c->length - c->offset, *size);
	if(*size > 0)
	{
		memcpy(buf, c->pBuffer + c->offset, *size);
		c->offset += *size;
	}
	return SZ_OK;
}

static ResultCode GetEncoderResult(SRes res)
{
	switch(res)
	{
	case SZ_OK: return StatusCode_Ok;
	case SZ_ERROR_MEM: return ErrorCode_Memory;
	case SZ_ERROR_PARAM: return ErrorCode_Parameter;
	case SZ_ERROR_OUTPUT_EOF: return ErrorCode_OutputEnd;
	case SZ_ERROR_THREAD: return ErrorCode_Threading;
	default: return ErrorCode_Unknown;
	}
}

static ResultCode GetDecoderResult(SRes res)
{
	switch(res)
	{
	case SZ_OK: return StatusCode_Ok;
	case SZ_ERROR_DATA: return ErrorCode_Data;
	case SZ_ERROR_MEM: return ErrorCode_Memory;
	case SZ_ERROR_UNSUPPORTED: return ErrorCode_Unsupported;
	case SZ_ERROR_INPUT_EOF: return ErrorCode_InputEnd;
	default: return ErrorCode_Unknown;
	}
}

ResultCode NativeLzmaCompressMemory(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t srcLen, unsigned char *outProps, size_t *outPropsSize,
	int level, unsigned dictSize, int lc, int lp, int pb, int algo, int fb, int btMode, int numHashBytes, unsigned mc, unsigned endMark, int numThreads)
{
	return GetEncoderResult(LzmaCompress(dest, destLen, src, srcLen, outProps, outPropsSize, level, dictSize, lc, lp, pb, fb, numThreads));
}

ResultCode NativeLzmaCompressMemory_V1(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t srcLen, unsigned char *outProps, size_t *outPropsSize,
	int level, unsigned dictSize, int lc, int lp, int pb, int algo, int fb, int btMode, int numHashBytes, unsigned mc, unsigned endMark, int numThreads)
{
	return NativeLzmaCompressStream(dest, destLen, src, srcLen, outProps, outPropsSize, level, dictSize, lc, lp, pb, algo, fb, btMode, numHashBytes, mc, endMark, numThreads);
}

ResultCode NativeLzmaCompressStream(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t srcLen, unsigned char *outProps, size_t *outPropsSize,
	int level, unsigned dictSize, int lc, int lp, int pb, int algo, int fb, int btMode, int numHashBytes, unsigned mc, unsigned endMark, int numThreads)
{
	CLzmaEncHandle handle = LzmaEnc_Create(&g_Alloc);
	CLzmaEncProps props;
	LzmaEncProps_Init(&props);
	props.level = level;
	props.dictSize = dictSize;
	props.lc = lc;
	props.lp = lp;
	props.pb = pb;
	props.algo = algo;
	props.fb = fb;
	props.btMode = btMode;
	props.numHashBytes = numHashBytes;
	props.mc = mc;
	props.writeEndMark = endMark;
	props.numThreads = numThreads;
	SRes res = LzmaEnc_SetProps(handle, &props);
	if(res == SZ_OK)
	{
		OutContext oc(NativeLzmaCompress2_Write, dest, *destLen);
		InContext ic(NativeLzmaCompress2_Read, src, srcLen);
		res = LzmaEnc_Encode(handle, &oc, &ic, NULL, &g_Alloc, &g_Alloc);
		if(res == SZ_OK)
		{
			*destLen = oc.offset;
			res = LzmaEnc_WriteProperties(handle, outProps, outPropsSize);
		}
	}
	LzmaEnc_Destroy(handle, &g_Alloc, &g_Alloc);
	return GetEncoderResult(res);
}

ResultCode NativeLzmaUncompress(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t *srcLen, const unsigned char *props, size_t propsSize)
{
	return GetDecoderResult(LzmaUncompress(dest, destLen, src, srcLen, props, propsSize));
}

ResultCode NativeLzmaUncompress_V1(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t *srcLen, const unsigned char *props, size_t propsSize, bool endMark)
{
	SRes res;
	CLzmaDec dec;
	LzmaDec_Construct(&dec);
	res = LzmaDec_Allocate(&dec, props, propsSize, &g_Alloc);
	if(res != SZ_OK)
		return ErrorCode_Unknown;
	LzmaDec_Init(&dec);

	SizeT writtenTotal = 0;
	SizeT usedTotal = 0;
	for(;;)
	{
		SizeT written = *destLen;
		SizeT used = *srcLen;

		ELzmaStatus status;
		res = LzmaDec_DecodeToBuf(&dec, dest, &written, src, &used, LZMA_FINISH_END, &status);
		if(res != SZ_OK)
			return ErrorCode_Unknown;

		writtenTotal += written;
		dest += written;
		*destLen -= written;
		usedTotal += used;
		src += used;
		*srcLen -= used;

		if(status == LZMA_STATUS_NEEDS_MORE_INPUT || status == LZMA_STATUS_NOT_FINISHED)
			continue;

		if(status == LZMA_STATUS_FINISHED_WITH_MARK)
		{
			if(endMark)
				break;
			else
				return ErrorCode_Unknown;
		}

		if(status == LZMA_STATUS_MAYBE_FINISHED_WITHOUT_MARK)
		{
			if(endMark)
				break;
		}

		return ErrorCode_Unknown;
	}

	LzmaDec_Free(&dec, &g_Alloc);

	*destLen = writtenTotal;
	*srcLen = usedTotal;

	return StatusCode_Ok;
}

ResultCode NativeLzmaCompress2(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t srcLen, unsigned char *outProp,
	int level, unsigned dictSize, int lc, int lp, int pb, int algo, int fb, int btMode, int numHashBytes, unsigned mc, unsigned endMark, int numThreads, int blockSize, int blockThreads, int totalThreads)
{
	CLzma2EncHandle handle = Lzma2Enc_Create(&g_Alloc, &g_Alloc);
	CLzma2EncProps props;
	Lzma2EncProps_Init(&props);
	props.lzmaProps.level = level;
	props.lzmaProps.dictSize = dictSize;
	props.lzmaProps.lc = lc;
	props.lzmaProps.lp = lp;
	props.lzmaProps.pb = pb;
	props.lzmaProps.algo = algo;
	props.lzmaProps.fb = fb;
	props.lzmaProps.btMode = btMode;
	props.lzmaProps.numHashBytes = numHashBytes;
	props.lzmaProps.mc = mc;
	props.lzmaProps.writeEndMark = endMark;
	props.lzmaProps.numThreads = numThreads;
	props.blockSize = blockSize;
	props.numBlockThreads = blockThreads;
	props.numTotalThreads = totalThreads;
	SRes res = Lzma2Enc_SetProps(handle, &props);
	if(res == SZ_OK)
	{
		OutContext oc(NativeLzmaCompress2_Write, dest, *destLen);
		InContext ic(NativeLzmaCompress2_Read, src, srcLen);
		res = Lzma2Enc_Encode(handle, &oc, &ic, NULL);
		*destLen = oc.offset;
		*outProp = Lzma2Enc_WriteProperties(handle);
	}
	Lzma2Enc_Destroy(handle);
	return GetEncoderResult(res);
}

ResultCode NativeLzmaUncompress2(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t *srcLen, unsigned char prop, int endMark)
{
	ELzmaStatus status;
	SRes res = Lzma2Decode(dest, destLen, src, srcLen, prop, LZMA_FINISH_END, &status, &g_Alloc);
	switch(status)
	{
	case LZMA_STATUS_FINISHED_WITH_MARK:
		if(!endMark)
			return ErrorCode_Unknown;
		break;
	case LZMA_STATUS_MAYBE_FINISHED_WITHOUT_MARK:
		if(endMark)
			return ErrorCode_Unknown;
		break;
	default:
		return ErrorCode_Unknown;
	}
	switch(res)
	{
	case SZ_OK: return StatusCode_Ok;
	case SZ_ERROR_DATA: return ErrorCode_Data;
	case SZ_ERROR_MEM: return ErrorCode_Memory;
	case SZ_ERROR_UNSUPPORTED: return ErrorCode_Unsupported;
	case SZ_ERROR_INPUT_EOF: return ErrorCode_InputEnd;
	default: return ErrorCode_Unknown;
	}
}

ResultCode NativeLzmaUncompress2_V1(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t *srcLen, unsigned char prop, int endMark)
{
	SRes res;
	CLzma2Dec dec;
	Lzma2Dec_Construct(&dec);
	res = Lzma2Dec_Allocate(&dec, prop, &g_Alloc);
	if(res != SZ_OK)
		return ErrorCode_Unknown;
	Lzma2Dec_Init(&dec);
	SizeT writtenTotal = 0;
	SizeT usedTotal = 0;
	for(;;)
	{
		SizeT written = *destLen;
		SizeT used = *srcLen;

		ELzmaStatus status;
		res = Lzma2Dec_DecodeToBuf(&dec, dest, &written, src, &used, LZMA_FINISH_END, &status);
		if(res != SZ_OK)
			return ErrorCode_Unknown;

		writtenTotal += written;
		dest += written;
		*destLen -= written;

		usedTotal += used;
		src += used;
		*srcLen -= used;

		if(status == LZMA_STATUS_NOT_FINISHED || status == LZMA_STATUS_NEEDS_MORE_INPUT)
			continue;

		if(status == LZMA_STATUS_FINISHED_WITH_MARK)
		{
			if(!endMark)
				return ErrorCode_Unknown;
			break;
		}

		if(status == LZMA_STATUS_MAYBE_FINISHED_WITHOUT_MARK)
		{
			if(endMark)
				return ErrorCode_Unknown;
			break;
		}

		return ErrorCode_Unknown;
	}
	Lzma2Dec_Free(&dec, &g_Alloc);
	*destLen = writtenTotal;
	*srcLen = usedTotal;
	return StatusCode_Ok;
}
