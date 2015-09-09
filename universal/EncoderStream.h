#pragma once

struct HelperContainer;

namespace ManagedLzma
{
	namespace Universal
	{
		public ref class EncoderStream sealed : public Windows::Storage::Streams::IOutputStream
		{
		public:
			EncoderStream(Windows::Storage::Streams::IOutputStream^ output);
			virtual ~EncoderStream();

			virtual Windows::Foundation::IAsyncOperation<bool>^ FlushAsync();
			virtual Windows::Foundation::IAsyncOperationWithProgress<uint32_t, uint32_t>^ WriteAsync(Windows::Storage::Streams::IBuffer^ buffer);

		internal:
			SRes ReadStream(void* buf, size_t* size);
			size_t WriteStream(const void* buf, size_t size);
			SRes ReportProgress(uint64_t inSize, uint64_t outSize);

		private:
			void StartEncoding();

			struct WriteRequest : public std::enable_shared_from_this<WriteRequest>
			{
				WriteRequest(
					uint64_t offset,
					Windows::Storage::Streams::IBuffer^ pBuffer,
					concurrency::progress_reporter<uint32_t> progress,
					concurrency::cancellation_token ct,
					HRESULT& hr)
					: mProgressOffset(offset)
					, mBufferOffset(0)
					, mBufferLength(pBuffer->Length)
					, pBuffer(pBuffer)
					, mProgress(progress)
					, mCancellation(ct)
				{
					if (FAILED(hr = reinterpret_cast<IUnknown*>(pBuffer)->QueryInterface(IID_PPV_ARGS(&pBufferAccess))))
						pBufferAccess.Reset();
				}

				uint64_t mProgressOffset;
				uint32_t mBufferOffset;
				uint32_t mBufferLength;
				Windows::Storage::Streams::IBuffer^ pBuffer;
				Microsoft::WRL::ComPtr<Windows::Storage::Streams::IBufferByteAccess> pBufferAccess;
				concurrency::progress_reporter<uint32_t> mProgress;
				concurrency::cancellation_token mCancellation;
				concurrency::task_completion_event<uint32_t> mCompletion;
			};

			uint64_t mInputOffset;
			uint64_t mOutputOffset;
			Windows::Storage::Streams::IOutputStream^ mOutput;
			CLzmaEncHandle mEncoder;
			concurrency::task<void> mEncoderTask;
			std::unique_ptr<HelperContainer> mHelper;
			std::queue<std::shared_ptr<WriteRequest>> mRequestQueue;
			bool mFlush;
		};
	}
}
