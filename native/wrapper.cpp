#include <msclr\marshal.h>
#include "wrapper.h"
#include "native.h"
#include "Trace.h"

using namespace System::IO;
using namespace System::Threading;
using namespace ManagedLzma::Testing;
using namespace ManagedLzma::LZMA::Reference::Native;

static void TraceInit(Guid^ id)
{
	String^ key = "\\\\.\\pipe\\LZMA\\TEST\\" + id->ToString("N");
	msclr::interop::marshal_context ctx;
	TraceInit(ctx.marshal_as<const char*>(key));
}

Helper::Helper(Guid^ id)
{
	TraceInit(id);
}

Helper::~Helper()
{
	TraceStop();
}

void Helper::LzmaCompress(SharedSettings^ s)
{
	switch(s->Variant)
	{
	case 1:
		{
			pin_ptr<byte> pDest = &s->Dst.Buffer[s->Dst.Offset];
			pin_ptr<byte> pSrc = &s->Src.Buffer[s->Src.Offset];
			byte props[5];
			size_t actualDestLen = s->Dst.Length;
			size_t actualPropsLen = 5;
			switch(NativeLzmaCompressMemory_V1(pDest, &actualDestLen, pSrc, s->Src.Length, props, &actualPropsLen,
				s->ActualLevel, s->ActualDictSize, s->ActualLC, s->ActualLP, s->ActualPB, s->ActualAlgo, s->ActualFB, s->ActualBTMode, s->ActualNumHashBytes, s->ActualMC, s->ActualWriteEndMark, s->ActualNumThreads))
			{
			case StatusCode_Ok: break;
			case ErrorCode_Memory: throw gcnew OutOfMemoryException("Memory allocation error");
			case ErrorCode_Parameter: throw gcnew ArgumentException("Incorrect paramater");
			case ErrorCode_OutputEnd: throw gcnew InternalBufferOverflowException("output buffer overflow");
			case ErrorCode_Threading: throw gcnew ThreadStateException("errors in multithreading functions");
			default: throw gcnew Exception();
			}
			s->WrittenSize = actualDestLen;
			s->Enc = PZ(gcnew array<byte>(actualPropsLen));
			for(size_t i = 0; i < actualPropsLen; ++i)
				s->Enc[i] = props[i];
		}
		break;
	default:
		{
			pin_ptr<byte> pDest = &s->Dst.Buffer[s->Dst.Offset];
			pin_ptr<byte> pSrc = &s->Src.Buffer[s->Src.Offset];
			byte props[5];
			size_t actualDestLen = s->Dst.Length;
			size_t actualPropsLen = 5;
			switch(NativeLzmaCompressMemory(pDest, &actualDestLen, pSrc, s->Src.Length, props, &actualPropsLen,
				s->ActualLevel, s->ActualDictSize, s->ActualLC, s->ActualLP, s->ActualPB, s->ActualAlgo, s->ActualFB, s->ActualBTMode, s->ActualNumHashBytes, s->ActualMC, s->ActualWriteEndMark, s->ActualNumThreads))
			{
			case StatusCode_Ok: break;
			case ErrorCode_Memory: throw gcnew OutOfMemoryException("Memory allocation error");
			case ErrorCode_Parameter: throw gcnew ArgumentException("Incorrect paramater");
			case ErrorCode_OutputEnd: throw gcnew InternalBufferOverflowException("output buffer overflow");
			case ErrorCode_Threading: throw gcnew ThreadStateException("errors in multithreading functions");
			default: throw gcnew Exception();
			}
			s->WrittenSize = actualDestLen;
			s->Enc = PZ(gcnew array<byte>(actualPropsLen));
			for(size_t i = 0; i < actualPropsLen; ++i)
				s->Enc[i] = props[i];
		}
		break;
	}
}

void Helper::LzmaUncompress(SharedSettings^ s)
{
	switch(s->Variant)
	{
	case 1:
		{
			pin_ptr<byte> pDest = &s->Dst.Buffer[s->Dst.Offset];
			pin_ptr<byte> pSrc = &s->Src.Buffer[s->Src.Offset];
			pin_ptr<byte> pProps = &s->Enc.Buffer[s->Enc.Offset];
			size_t actualDestLen = s->Dst.Length;
			size_t actualSrcLen = s->Src.Length;
			switch(NativeLzmaUncompress_V1(pDest, &actualDestLen, pSrc, &actualSrcLen, pProps, s->Enc.Length, s->ActualWriteEndMark != 0))
			{
			case StatusCode_Ok: break;
			case ErrorCode_Data: throw gcnew InvalidDataException("Data error");
			case ErrorCode_Memory: throw gcnew OutOfMemoryException("Memory allocation arror");
			case ErrorCode_Unsupported: throw gcnew NotSupportedException("Unsupported properties");
			case ErrorCode_InputEnd: throw gcnew EndOfStreamException("it needs more bytes in input buffer (src)");
			default: throw gcnew Exception();
			}
			s->WrittenSize = actualDestLen;
			s->UsedSize = actualSrcLen;
		}
		break;
	default:
		{
			pin_ptr<byte> pDest = &s->Dst.Buffer[s->Dst.Offset];
			pin_ptr<byte> pSrc = &s->Src.Buffer[s->Src.Offset];
			pin_ptr<byte> pProps = &s->Enc.Buffer[s->Enc.Offset];
			size_t actualDestLen = s->Dst.Length;
			size_t actualSrcLen = s->Src.Length;
			switch(NativeLzmaUncompress(pDest, &actualDestLen, pSrc, &actualSrcLen, pProps, s->Enc.Length))
			{
			case StatusCode_Ok: break;
			case ErrorCode_Data: throw gcnew InvalidDataException("Data error");
			case ErrorCode_Memory: throw gcnew OutOfMemoryException("Memory allocation arror");
			case ErrorCode_Unsupported: throw gcnew NotSupportedException("Unsupported properties");
			case ErrorCode_InputEnd: throw gcnew EndOfStreamException("it needs more bytes in input buffer (src)");
			default: throw gcnew Exception();
			}
			s->WrittenSize = actualDestLen;
			s->UsedSize = actualSrcLen;
		}
		break;
	}
}

Helper2::Helper2(Guid^ id)
{
	TraceInit(id);
}

Helper2::~Helper2()
{
	TraceStop();
}

void Helper2::LzmaCompress(SharedSettings^ s)
{
	pin_ptr<byte> pDest = &s->Dst.Buffer[s->Dst.Offset];
	pin_ptr<byte> pSrc = &s->Src.Buffer[s->Src.Offset];
	byte prop;
	size_t actualDestLen = s->Dst.Length;
	switch(NativeLzmaCompress2(pDest, &actualDestLen, pSrc, s->Src.Length, &prop, s->ActualLevel, s->ActualDictSize, s->ActualLC, s->ActualLP, s->ActualPB, s->ActualAlgo, s->ActualFB, s->ActualBTMode, s->ActualNumHashBytes, s->ActualMC, s->ActualWriteEndMark, s->ActualNumThreads, s->ActualBlockSize, s->ActualNumBlockThreads, s->ActualNumTotalThreads))
	{
	case StatusCode_Ok: break;
	case ErrorCode_Memory: throw gcnew OutOfMemoryException("Memory allocation error");
	case ErrorCode_Parameter: throw gcnew ArgumentException("Incorrect paramater");
	case ErrorCode_OutputEnd: throw gcnew InternalBufferOverflowException("output buffer overflow");
	case ErrorCode_Threading: throw gcnew ThreadStateException("errors in multithreading functions");
	default: throw gcnew Exception();
	}
	s->WrittenSize = actualDestLen;
	s->Enc = PZ(gcnew array<byte>(1));
	s->Enc[0] = prop;
}

void Helper2::LzmaUncompress(SharedSettings^ s)
{
	if(s->Enc.Length != 1)
		throw gcnew ArgumentException("Properties must contain exactly one byte.", "propsSize");
	pin_ptr<byte> pDest = &s->Dst.Buffer[s->Dst.Offset];
	pin_ptr<byte> pSrc = &s->Src.Buffer[s->Src.Offset];
	size_t actualDestLen = s->Dst.Length;
	size_t actualSrcLen = s->Src.Length;
	ResultCode res;
	if(s->Variant == 1)
		res = NativeLzmaUncompress2_V1(pDest, &actualDestLen, pSrc, &actualSrcLen, s->Enc[0], s->ActualWriteEndMark);
	else
		res = NativeLzmaUncompress2(pDest, &actualDestLen, pSrc, &actualSrcLen, s->Enc[0], s->ActualWriteEndMark);
	switch(res)
	{
	case StatusCode_Ok: break;
	case ErrorCode_Data: throw gcnew InvalidDataException("Data error");
	case ErrorCode_Memory: throw gcnew OutOfMemoryException("Memory allocation arror");
	case ErrorCode_Unsupported: throw gcnew NotSupportedException("Unsupported properties");
	case ErrorCode_InputEnd: throw gcnew EndOfStreamException("it needs more bytes in input buffer (src)");
	default: throw gcnew Exception();
	}
	s->WrittenSize = actualDestLen;
	s->UsedSize = actualSrcLen;
}
