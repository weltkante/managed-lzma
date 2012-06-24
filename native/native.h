#pragma once

enum ResultCode
{
	StatusCode_Ok,
	ErrorCode_Data,
	ErrorCode_Memory,
	ErrorCode_Parameter,
	ErrorCode_Threading,
	ErrorCode_Unsupported,
	ErrorCode_OutputEnd,
	ErrorCode_InputEnd,
	ErrorCode_Unknown,
};

ResultCode NativeLzmaCompressMemory(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t srcLen, unsigned char *outProps, size_t *outPropsSize,
	int level, unsigned dictSize, int lc, int lp, int pb, int algo, int fb, int btMode, int numHashBytes, unsigned mc, unsigned endMark, int numThreads);
ResultCode NativeLzmaCompressMemory_V1(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t srcLen, unsigned char *outProps, size_t *outPropsSize,
	int level, unsigned dictSize, int lc, int lp, int pb, int algo, int fb, int btMode, int numHashBytes, unsigned mc, unsigned endMark, int numThreads);
ResultCode NativeLzmaCompressStream(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t srcLen, unsigned char *outProps, size_t *outPropsSize,
	int level, unsigned dictSize, int lc, int lp, int pb, int algo, int fb, int btMode, int numHashBytes, unsigned mc, unsigned endMark, int numThreads);
ResultCode NativeLzmaUncompress(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t *srcLen, const unsigned char *props, size_t propsSize);
ResultCode NativeLzmaUncompress_V1(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t *srcLen, const unsigned char *props, size_t propsSize, bool endMark);

ResultCode NativeLzmaCompress2(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t srcLen, unsigned char *outProp,
	int level, unsigned dictSize, int lc, int lp, int pb, int algo, int fb, int btMode, int numHashBytes, unsigned mc, unsigned endMark, int numThreads, int blockSize, int blockThreads, int totalThreads);
ResultCode NativeLzmaUncompress2(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t *srcLen, unsigned char prop, int endMark);
ResultCode NativeLzmaUncompress2_V1(unsigned char *dest, size_t *destLen, const unsigned char *src, size_t *srcLen, unsigned char prop, int endMark);
