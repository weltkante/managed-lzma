#pragma once

using namespace System;
using namespace System::Runtime::InteropServices;

typedef System::Byte byte;
typedef System::UInt32 uint;

namespace ManagedLzma {
namespace LZMA {
namespace Reference {
namespace Native {

	public ref class Helper
		: public IHelper
	{
	public:
		Helper(Guid^ id);
		~Helper();
		virtual void LzmaCompress(SharedSettings^ s);
		virtual void LzmaUncompress(SharedSettings^ s);
	};

	public ref class Helper2
		: public IHelper
	{
	public:
		Helper2(Guid^ id);
		~Helper2();
		virtual void LzmaCompress(SharedSettings^ s);
		virtual void LzmaUncompress(SharedSettings^ s);
	};

} } } }
