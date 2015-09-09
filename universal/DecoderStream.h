#pragma once

#include "Native/LzmaDec.h"

namespace ManagedLzma
{
	namespace Universal
	{
		ref class DecoderSettings;

		public ref class DecoderStream sealed : public Windows::Storage::Streams::IInputStream
		{
		public:
			DecoderStream(Windows::Storage::Streams::IInputStream^ input, DecoderSettings^ settings);
			virtual ~DecoderStream();

			virtual Windows::Foundation::IAsyncOperationWithProgress<Windows::Storage::Streams::IBuffer^, uint32_t>^ ReadAsync(
				Windows::Storage::Streams::IBuffer^ buffer, uint32_t count, Windows::Storage::Streams::InputStreamOptions options);

		private:
			Windows::Storage::Streams::IInputStream^ mInput;
			DecoderSettings^ mSettings;
			CLzmaDec mDecoder;
		};
	}
}
