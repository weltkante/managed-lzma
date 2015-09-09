#pragma once

namespace ManagedLzma
{
	namespace Universal
	{
		ref class EncoderInput;
		ref class EncoderOutput;

		public value class EncoderProgress
		{
		public:
			int64_t BytesRead;
			int64_t BytesWritten;
		};

		public ref class Encoder sealed
		{
		public:
			Encoder();
			virtual ~Encoder();

			Windows::Foundation::IAsyncActionWithProgress<EncoderProgress>^ GetOperation();
			Windows::Storage::Streams::IOutputStream^ GetInputStream();
			Windows::Storage::Streams::IInputStream^ GetOutputStream();

		internal:
			Windows::Foundation::IAsyncOperation<bool>^ FlushAsync();
			Windows::Foundation::IAsyncOperationWithProgress<uint32_t, uint32_t>^ WriteAsync(Windows::Storage::Streams::IBuffer^ buffer);
			Windows::Foundation::IAsyncOperationWithProgress<Windows::Storage::Streams::IBuffer^, uint32_t>^ ReadAsync(
				Windows::Storage::Streams::IBuffer^ buffer, uint32_t length, Windows::Storage::Streams::InputStreamOptions options);

		private:
			void StartOperation();

			struct InputRequest
			{
				Windows::Storage::Streams::IBuffer^ pBuffer;
				concurrency::progress_reporter<uint32_t> mProgress;
				concurrency::task_completion_event<uint32_t> mCompletion;
			};

			struct OutputRequest
			{
				Windows::Storage::Streams::IBuffer^ pBuffer;
				uint32_t mLength;
				Windows::Storage::Streams::InputStreamOptions mOptions;
				concurrency::progress_reporter<uint32_t> mProgress;
				concurrency::task_completion_event<Windows::Storage::Streams::IBuffer^> mCompletion;
			};

			CLzmaEncHandle mEncoder;
			EncoderInput^ mInputStream;
			EncoderOutput^ mOutputStream;
			Windows::Foundation::IAsyncActionWithProgress<EncoderProgress>^ mOperation;
			concurrency::progress_reporter<EncoderProgress> mProgress;
			concurrency::cancellation_token mCancellation;
			std::queue<std::shared_ptr<InputRequest>> mInputRequests;
			std::queue<std::shared_ptr<OutputRequest>> mOutputRequests;
			concurrency::task_completion_event<void> mInputSignal;
			concurrency::task_completion_event<void> mCompletionSignal;
			bool mComplete;
		};

		private ref class EncoderInput sealed : public Windows::Storage::Streams::IOutputStream
		{
		public:
			virtual ~EncoderInput();
			virtual Windows::Foundation::IAsyncOperation<bool>^ FlushAsync();
			virtual Windows::Foundation::IAsyncOperationWithProgress<uint32_t, uint32_t>^ WriteAsync(Windows::Storage::Streams::IBuffer^ buffer);

		internal:
			EncoderInput(Encoder^ encoder);

		private:
			Encoder^ mEncoder;
		};

		private ref class EncoderOutput sealed : public Windows::Storage::Streams::IInputStream
		{
		public:
			virtual ~EncoderOutput();
			virtual Windows::Foundation::IAsyncOperationWithProgress<Windows::Storage::Streams::IBuffer^, uint32_t>^ ReadAsync(
				Windows::Storage::Streams::IBuffer^ buffer, uint32_t length, Windows::Storage::Streams::InputStreamOptions options);

		internal:
			EncoderOutput(Encoder^ encoder);

		private:
			Encoder^ mEncoder;
		};
	}
}
