#include "pch.h"
#include "Encoder.h"
#include "Utilities.h"
#include <windows.storage.streams.h>

#include "Native/LzmaEnc.h"

using namespace Platform;
using namespace Microsoft::WRL;
using namespace Windows::Foundation;
using namespace Windows::Storage::Streams;
using namespace ManagedLzma::Universal;

Encoder::Encoder()
	: mInputStream(ref new EncoderInput(this))
	, mOutputStream(ref new EncoderOutput(this))
	, mCancellation(concurrency::cancellation_token::none())
{
	StartOperation();
}

Encoder::~Encoder()
{
}

IAsyncActionWithProgress<EncoderProgress>^ Encoder::GetOperation()
{
	return mOperation;
}

IOutputStream^ Encoder::GetInputStream()
{
	return mInputStream;
}

IInputStream^ Encoder::GetOutputStream()
{
	return mOutputStream;
}

void Encoder::StartOperation()
{
	mOperation = concurrency::create_async([this](concurrency::progress_reporter<EncoderProgress> progress, concurrency::cancellation_token ct) {
		mProgress = progress;
		mCancellation = ct;

		concurrency::create_task(mInputSignal).then([this](concurrency::task<void> task) {
			if (mComplete) {
				mCompletionSignal.set();
				return;
			}

			// TODO: encode the available input
			//LzmaEncode();

			// create a new input signal and wait for available input
			mInputSignal = concurrency::task_completion_event<void>();
		});

		return concurrency::create_task(mCompletionSignal);
	});
}

IAsyncOperation<bool>^ Encoder::FlushAsync()
{
	return concurrency::create_async([this] {
		mComplete = true;
		return false;
	});
}

IAsyncOperationWithProgress<uint32_t, uint32_t>^ Encoder::WriteAsync(IBuffer^ buffer)
{
	return concurrency::create_async([this, buffer](concurrency::progress_reporter<uint32_t> progress) {
		auto request = std::make_shared<InputRequest>();
		request->pBuffer = buffer;
		auto completion = request->mCompletion;
		mInputRequests.push(std::move(request));
		mInputSignal.set();
		return concurrency::create_task(std::move(completion));
	});
}

IAsyncOperationWithProgress<IBuffer^, uint32_t>^ Encoder::ReadAsync(IBuffer^ buffer, uint32_t length, InputStreamOptions options)
{
	return concurrency::create_async([this, buffer, length, options](concurrency::progress_reporter<uint32_t> progress) {
		auto request = std::make_shared<OutputRequest>();
		request->pBuffer = buffer;
		request->mLength = length;
		request->mOptions = options;
		auto completion = request->mCompletion;
		mOutputRequests.push(std::move(request));
		return concurrency::create_task(std::move(completion));
	});
}

EncoderInput::EncoderInput(Encoder^ encoder)
	: mEncoder(encoder)
{
}

EncoderInput::~EncoderInput()
{
}

IAsyncOperation<bool>^ EncoderInput::FlushAsync()
{
	return mEncoder->FlushAsync();
}

IAsyncOperationWithProgress<uint32_t, uint32_t>^ EncoderInput::WriteAsync(IBuffer^ buffer)
{
	return mEncoder->WriteAsync(buffer);
}

EncoderOutput::EncoderOutput(Encoder^ encoder)
	: mEncoder(encoder)
{
}

EncoderOutput::~EncoderOutput()
{
}

IAsyncOperationWithProgress<IBuffer^, uint32_t>^ EncoderOutput::ReadAsync(IBuffer^ buffer, uint32_t length, InputStreamOptions options)
{
	return mEncoder->ReadAsync(buffer, length, options);
}
