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
		: public Testing::IHelper
	{
	public:
		Helper(Guid^ id);
		~Helper();
		virtual void LzmaCompress(Testing::SharedSettings^ s);
		virtual void LzmaUncompress(Testing::SharedSettings^ s);
	};

	public ref class Helper2
		: public Testing::IHelper
	{
	public:
		Helper2(Guid^ id);
		~Helper2();
		virtual void LzmaCompress(Testing::SharedSettings^ s);
		virtual void LzmaUncompress(Testing::SharedSettings^ s);
	};

} } } }
