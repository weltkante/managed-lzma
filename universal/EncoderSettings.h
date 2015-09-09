#pragma once

namespace ManagedLzma
{
	namespace Universal
	{
		ref class DecoderSettings;

		public ref class EncoderSettings sealed
		{
		public:
			EncoderSettings();
			void SetLevel(int level);
			DecoderSettings^ GetDecoderSettings();

			property int64_t ReduceSize { int64_t get() { return mReduceSize; } void set(int64_t value); }
			property int32_t DictionarySize { int32_t get() { return mDictionarySize; } void set(int32_t value); }
			property int LC { int get() { return mLC; } void set(int value); }
			property int LP { int get() { return mLP; } void set(int value); }
			property int PB { int get() { return mPB; } void set(int value); }
			property bool FastMode { bool get() { return mFastMode; } void set(bool value); }
			property int FB { int get() { return mFB; } void set(int value); }
			property bool BinaryTreeMode { bool get() { return mBinaryTreeMode; } void set(bool value); }
			property int HashBytes { int get() { return mHashBytes; } void set(int value); }
			property int MC { int get() { return mMC; } void set(int value); }
			property bool WriteEndMark { bool get() { return mWriteEndMark; } void set(bool value); }
			property int MaxThreadCount { int get(); }
			property int ThreadCount { int get() { return mThreadCount; } void set(int value); }

		private:
			int64_t mReduceSize;
			int32_t mDictionarySize;
			int32_t mLC;
			int32_t mLP;
			int32_t mPB;
			int32_t mFB;
			int32_t mHashBytes;
			int32_t mMC;
			int32_t mThreadCount;
			bool mFastMode;
			bool mBinaryTreeMode;
			bool mWriteEndMark;
		};
	}
}
