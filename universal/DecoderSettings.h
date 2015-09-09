#pragma once

namespace ManagedLzma
{
	namespace Universal
	{
		public ref class DecoderSettings sealed
		{
		public:
			static DecoderSettings^ FromArray(const Platform::Array<byte>^ buffer);
			static DecoderSettings^ FromBuffer(Windows::Storage::Streams::IBuffer^ buffer);
			
			Platform::Array<byte>^ ToArray();

		internal:
			DecoderSettings(byte lc, byte lp, byte pb, uint32_t dictionarySize);
			void WriteTo(byte* p);

		private:
			uint32_t mDictionarySize;
			byte mLC;
			byte mLP;
			byte mPB;
		};
	}
}
