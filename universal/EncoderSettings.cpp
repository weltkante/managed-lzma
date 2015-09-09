#include "pch.h"
#include "EncoderSettings.h"
#include "DecoderSettings.h"
#include "Native/LzmaEnc.h"

using namespace ManagedLzma::Universal;
using namespace Platform;

EncoderSettings::EncoderSettings()
	: mReduceSize(INT64_MAX)
	, mWriteEndMark(false)
{
	SetLevel(5);
}

void EncoderSettings::SetLevel(int level)
{
	if (level < 0 || level > 9)
		throw ref new InvalidArgumentException();

	if (level <= 5)
		mDictionarySize = (1 << (level * 2 + 14));
	else if (level == 6)
		mDictionarySize = (1 << 25);
	else
		mDictionarySize = (1 << 26);

	mLC = 3;
	mLP = 0;
	mPB = 2;
	mFastMode = (level <= 4);
	mFB = (level <= 6) ? 32 : 64;
	mBinaryTreeMode = !mFastMode;
	mHashBytes = 4;

	if (level <= 4)
		mMC = 16;
	else if (level <= 6)
		mMC = 32;
	else
		mMC = 48;

	mThreadCount = BinaryTreeMode && !FastMode ? 2 : 1;
}

DecoderSettings^ EncoderSettings::GetDecoderSettings()
{
	return ref new DecoderSettings(mLC, mLP, mPB, mDictionarySize);
}

void EncoderSettings::ReduceSize::set(int64_t value)
{
	throw ref new NotImplementedException();
}

void EncoderSettings::DictionarySize::set(int32_t value)
{
	if (value <= 0)
		throw ref new InvalidArgumentException();

	throw ref new NotImplementedException();
}

void EncoderSettings::LC::set(int value)
{
	if (value < 0 || value > 8)
		throw ref new InvalidArgumentException();

	mLC = value;
}

void EncoderSettings::LP::set(int value)
{
	if (value < 0 || value > 4)
		throw ref new InvalidArgumentException();

	mLP = value;
}

void EncoderSettings::PB::set(int value)
{
	if (value < 0 || value > 4)
		throw ref new InvalidArgumentException();

	mPB = value;
}

void EncoderSettings::FastMode::set(bool value)
{
	mFastMode = value;
}

void EncoderSettings::FB::set(int value)
{
	if (value < 5 || value > 273)
		throw ref new InvalidArgumentException();
}

void EncoderSettings::BinaryTreeMode::set(bool value)
{
	mBinaryTreeMode = value;
}

void EncoderSettings::HashBytes::set(int value)
{
	if (value < 2 || value > 4)
		throw ref new InvalidArgumentException();

	mHashBytes = value;
}

void EncoderSettings::MC::set(int value)
{
	if (value < 1 || value > (1 << 30))
		throw ref new InvalidArgumentException();

	mMC = value;
}

void EncoderSettings::WriteEndMark::set(bool value)
{
	mWriteEndMark = value;
}

int EncoderSettings::MaxThreadCount::get()
{
	return 2;
}

void EncoderSettings::ThreadCount::set(int value)
{
	if (value < 1 || value > MaxThreadCount)
		throw ref new InvalidArgumentException();

	mThreadCount = value;
}
