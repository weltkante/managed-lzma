#pragma once

#include "Native\LzmaDec.h"

namespace ManagedLzma
{
	namespace Universal
	{
		ref class Decoder;
		ref class DecoderSettings;
		ref class DecoderInputStream;
		ref class DecoderOutputStream;

		public ref class DecoderInputStream sealed : Windows::Storage::Streams::IOutputStream
		{
		public:
			DecoderInputStream(Decoder^ decoder);
			virtual ~DecoderInputStream();

			virtual Windows::Foundation::IAsyncOperation<bool>^ FlushAsync();
			virtual Windows::Foundation::IAsyncOperationWithProgress<uint32_t, uint32_t>^ WriteAsync(Windows::Storage::Streams::IBuffer^ buffer);

		private:
			Decoder^ mDecoder;
		};

		public ref class DecoderOutputStream sealed : Windows::Storage::Streams::IInputStream
		{
		public:
			DecoderOutputStream(Decoder^ decoder);
			virtual ~DecoderOutputStream();

			virtual Windows::Foundation::IAsyncOperationWithProgress<Windows::Storage::Streams::IBuffer^, uint32_t>^ ReadAsync(
				Windows::Storage::Streams::IBuffer^ buffer, uint32_t count, Windows::Storage::Streams::InputStreamOptions options);

		private:
			Decoder^ mDecoder;
		};

		public ref class Decoder sealed
		{
		public:
			Decoder(DecoderSettings^ settings);
			virtual ~Decoder();

			Windows::Foundation::IAsyncAction^ CancelAsync();
			Windows::Foundation::IAsyncAction^ CompleteInputAsync();
			Windows::Foundation::IAsyncOperationWithProgress<uint32_t, uint32_t>^ ProvideInputAsync(Windows::Storage::Streams::IBuffer^ buffer);
			Windows::Foundation::IAsyncOperationWithProgress<Windows::Storage::Streams::IBuffer^, uint32_t>^ ReadOutputAsync(
				Windows::Storage::Streams::IBuffer^ buffer, uint32_t count, Windows::Storage::Streams::InputStreamOptions options);

		private:
			struct InputFrame : public std::enable_shared_from_this<InputFrame>
			{
				Windows::Storage::Streams::IBuffer^ mBuffer;
				size_t mOffset;
				size_t mEnding;
				concurrency::progress_reporter<uint32_t> mProgress;
				concurrency::task_completion_event<uint32_t> mCompletion;
			};

			struct OutputFrame : public std::enable_shared_from_this<OutputFrame>
			{
				Windows::Storage::Streams::IBuffer^ mBuffer;
				size_t mOffset;
				size_t mEnding;
				Windows::Storage::Streams::InputStreamOptions mOptions;
				concurrency::progress_reporter<uint32_t> mProgress;
				concurrency::task_completion_event<Windows::Storage::Streams::IBuffer^> mCompletion;
			};

			void PushInputFrame(const std::shared_ptr<InputFrame>& frame);
			void PushOutputFrame(const std::shared_ptr<OutputFrame>& frame);
			void TryStartOperation();
			void WriteOutput();
			bool DecodeInput();

			// immutable
			concurrency::critical_section mSync;
			DecoderSettings^ mSettings;

			// multithreaded access (under lock)
			concurrency::task<void> mOperation;
			std::queue<std::shared_ptr<InputFrame>> mInputQueue;
			std::queue<std::shared_ptr<OutputFrame>> mOutputQueue;
			size_t mTotalOutputCapacity;
			bool mStarted;
			bool mRunning;
			bool mFlushed;

			// owned by the currently running decoder thread
			CLzmaDec mDecoder;
			ELzmaStatus mDecoderState;
			size_t mDecoderPosition;
		};
	}
}
