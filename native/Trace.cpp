#include "trace.h"
#include <Windows.h>
#include <map>
#include <string>

#define QERROR() do { __debugbreak(); ExitProcess(0); } while(0)
#define QCHECK(x) do { if(!(x)) { QERROR(); } } while(0)

#define CMD_INIT_TRACE (10)
#define CMD_STRING_MAP (11)

#define CMD_THREAD_CTOR (100)
#define CMD_THREAD_DTOR (101)
#define CMD_THREAD_WAIT (102)

#define CMD_OBJECT_CTOR (110)
#define CMD_OBJECT_DTOR (111)
#define CMD_OBJECT_WAIT1 (112)

#define CMD_STATUS_CODE (120)

#define MATCH_INT_1 (0x01)
#define MATCH_STR_1 (0x02)
#define MATCH_INT_2 (0x04)
#define MATCH_STR_2 (0x08)
#define MATCH_INT_3 (0x10)
#define MATCH_STR_3 (0x20)
#define MATCH_ESCAPE (0x80)

typedef std::map<std::string, int> StringMap;

static __declspec(thread) struct TraceSession *mSession = NULL;
static __declspec(thread) struct TraceContext *mContext = NULL;

static void SetThreadName(DWORD dwThreadID, char* threadName)
{
#pragma pack(push, 8)
	struct THREADNAME_INFO
	{
		DWORD dwType;		// Must be 0x1000.
		LPCSTR szName;		// Pointer to name (in user addr space).
		DWORD dwThreadID;	// Thread ID (-1 = caller thread).
		DWORD dwFlags;		// Reserved for future use, must be zero.
	};
#pragma pack(pop)

	THREADNAME_INFO info;
	info.dwType = 0x1000;
	info.szName = threadName;
	info.dwThreadID = dwThreadID;
	info.dwFlags = 0;

	__try {
		const DWORD MS_VC_EXCEPTION = 0x406D1388;
		RaiseException(MS_VC_EXCEPTION, 0, sizeof(info) / sizeof(ULONG_PTR), (ULONG_PTR*)&info);
	} __except(EXCEPTION_EXECUTE_HANDLER) { }
}

struct Pipe
{
	HANDLE mHandle;

	Pipe()
		: mHandle(INVALID_HANDLE_VALUE)
	{
	}

	Pipe(std::string name, bool ack)
		: mHandle(INVALID_HANDLE_VALUE)
	{
		Initialize(name, ack);
	}

	~Pipe()
	{
		if(mHandle != INVALID_HANDLE_VALUE) Close();
	}

	void Initialize(const std::string &name, bool ack)
	{
		QCHECK(mHandle == INVALID_HANDLE_VALUE);
		mHandle = CreateNamedPipeA(name.c_str(), (ack?PIPE_ACCESS_DUPLEX:PIPE_ACCESS_OUTBOUND)|FILE_FLAG_FIRST_PIPE_INSTANCE, 0, 1, 0x1000, 0, 0, NULL);
		QCHECK(mHandle != INVALID_HANDLE_VALUE);
		QCHECK(ConnectNamedPipe(mHandle, NULL) || GetLastError() == ERROR_PIPE_CONNECTED);
	}

	void Flush()
	{
		QCHECK(mHandle != INVALID_HANDLE_VALUE);
		FlushFileBuffers(mHandle);
	}

	void Close()
	{
		QCHECK(mHandle != INVALID_HANDLE_VALUE);
		CloseHandle(mHandle);
		mHandle = INVALID_HANDLE_VALUE;
	}

	void Write(const void *data, int size)
	{
		DWORD processed;
		QCHECK(WriteFile(mHandle, data, size, &processed, NULL) != 0 && processed == size);
	}

	void WaitForAck(char cmd, int key)
	{
#pragma pack(push, 1)
		struct { char cmd; int key; } ack;
#pragma pack(pop)
		DWORD processed = 0;
		do {
			DWORD delta;
			QCHECK(ReadFile(mHandle, ((char*)&ack) + processed, sizeof(ack) - processed, &delta, NULL) != 0 && delta > 0);
			processed += delta;
		} while(processed < sizeof(ack));
		QCHECK(processed == sizeof(ack));
		QCHECK(ack.cmd == cmd && ack.key == key);
	}
};

struct TraceSession
{
	std::string mName;
	StringMap mStringMap;
	Pipe mPipe;
	CRITICAL_SECTION lock;
	unsigned char mSyncCount;

	TraceSession(const std::string &name)
		: mName(name)
		, mSyncCount(0xAB)
	{
		QCHECK(mSession == NULL);
		InitializeCriticalSection(&lock);
		mPipe.Initialize(mName + "\\root", false);
#pragma pack(push, 1)
		struct { char op; int thread; char sync; } record = { CMD_INIT_TRACE, GetCurrentThreadId(), mSyncCount++ };
#pragma pack(pop)
		mPipe.Write(&record, sizeof(record));
		mSession = this;
	}

	~TraceSession()
	{
		// Someone may still be writing to the pipe, so lock to make sure we are done;
		EnterCriticalSection(&lock);
		mPipe.Flush();
		mPipe.Close();
		mSession = NULL;
		LeaveCriticalSection(&lock);
		DeleteCriticalSection(&lock);
	}

	void Lock()
	{
		EnterCriticalSection(&lock);
	}

	void Unlock()
	{
		LeaveCriticalSection(&lock);
	}

	void WriteCmd(char op, int arg1)
	{
#pragma pack(push, 1)
		struct { char op; int thread, arg1; char sync; } record = { op, GetCurrentThreadId(), arg1, mSyncCount++ };
#pragma pack(pop)

		EnterCriticalSection(&lock);
		mPipe.Write(&record, sizeof(record));
		LeaveCriticalSection(&lock);
	}

	int MapStringInternal(const std::string &str)
	{
		StringMap::const_iterator it = mStringMap.find(str);
		if(it != mStringMap.end())
			return it->second;

		int key = mStringMap.size() + 1;
		mStringMap.insert(StringMap::value_type(str, key));

#pragma pack(push, 1)
		struct { char op; short len; } record = { CMD_STRING_MAP, str.size() };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.Write(str.c_str(), record.len);
		return key;
	}

	int WriteCmd(char op, const char *str1, int arg2)
	{
		int arg1 = 0;
		if(str1 == NULL)
		{
			EnterCriticalSection(&lock);
		}
		else
		{
			std::string str(str1);

			EnterCriticalSection(&lock);
			arg1 = MapStringInternal(str1);
		}

#pragma pack(push, 1)
		struct { char op; int thread, arg1, arg2; char sync; } record = { op, GetCurrentThreadId(), arg1, arg2, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		LeaveCriticalSection(&lock);
		return record.arg1;
	}

	int WriteCmd(char op, const void *handle, const char *str1)
	{
		int arg1 = 0;
		if(str1 == NULL)
		{
			EnterCriticalSection(&lock);
		}
		else
		{
			std::string str(str1);

			EnterCriticalSection(&lock);
			arg1 = MapStringInternal(str1);
		}

#pragma pack(push, 1)
		struct { char op; int thread, handle, arg1; char sync; } record = { op, GetCurrentThreadId(), reinterpret_cast<int>(handle), arg1, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		LeaveCriticalSection(&lock);
		return record.arg1;
	}

	void WriteCmd(char op, int arg1, int arg2)
	{
#pragma pack(push, 1)
		struct { char op; int thread, arg1, arg2; char sync; } record = { op, GetCurrentThreadId(), arg1, arg2, mSyncCount++ };
#pragma pack(pop)

		EnterCriticalSection(&lock);
		mPipe.Write(&record, sizeof(record));
		LeaveCriticalSection(&lock);
	}

	void WriteCmd(char op, const char *str1, int arg2, int arg3)
	{
		int arg1 = 0;
		if(str1 == NULL)
		{
			EnterCriticalSection(&lock);
		}
		else
		{
			std::string str(str1);

			EnterCriticalSection(&lock);
			arg1 = MapStringInternal(str1);
		}

#pragma pack(push, 1)
		struct { char op; int thread, arg1, arg2, arg3; char sync; } record = { op, GetCurrentThreadId(), arg1, arg2, arg3, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		LeaveCriticalSection(&lock);
	}

	void WriteCmd(char op, const void *handle, const char *str1, int arg2)
	{
		int arg1 = 0;
		if(str1 == NULL)
		{
			EnterCriticalSection(&lock);
		}
		else
		{
			std::string str(str1);

			EnterCriticalSection(&lock);
			arg1 = MapStringInternal(str1);
		}

#pragma pack(push, 1)
		struct { char op; int thread, handle, arg1, arg2; char sync; } record = { op, GetCurrentThreadId(), reinterpret_cast<int>(handle), arg1, arg2, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		LeaveCriticalSection(&lock);
	}
};

struct TraceContext
{
	StringMap mCache;
	Pipe mPipe;
	unsigned char mSyncCount;

	TraceContext()
		: mSyncCount(0xAB)
	{
		char name[MAX_PATH];
		sprintf_s(name, "%s\\%08x", mSession->mName.c_str(), GetCurrentThreadId());
		mPipe.Initialize(name, true);
	}

	~TraceContext()
	{
		mPipe.WaitForAck(-1, 0xBADF00D);
		mPipe.Flush();
		mPipe.Close();
	}

	int MapString(const char *arg)
	{
		if(arg == NULL)
			return 0;

		std::string str(arg);
		StringMap::const_iterator it = mCache.find(str);
		if(it != mCache.end())
			return it->second;

		TraceSession *x = mSession;
		LPCRITICAL_SECTION lock = &x->lock;
		EnterCriticalSection(lock);
		int key = x->MapStringInternal(str);
		LeaveCriticalSection(lock);

		mCache.insert(StringMap::value_type(str, key));
		return key;
	}

	void WriteMatch(int arg1)
	{
#pragma pack(push, 1)
		struct { char op; int arg1; char sync; } record = { MATCH_INT_1, arg1, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, arg1);
	}
	
	void WriteMatch(int arg1, int arg2)
	{
#pragma pack(push, 1)
		struct { char op; int arg1, arg2; char sync; } record = { MATCH_INT_1|MATCH_INT_2, arg1, arg2, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, arg1 ^ arg2);
	}
	
	void WriteMatch(int arg1, int arg2, int arg3)
	{
#pragma pack(push, 1)
		struct { char op; int arg1, arg2, arg3; char sync; } record = { MATCH_INT_1|MATCH_INT_2|MATCH_INT_3, arg1, arg2, arg3, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, arg1 ^ arg2 ^ arg3);
	}
	
	void WriteMatch(const char *arg1)
	{
#pragma pack(push, 1)
		struct { char op; int arg1; char sync; } record = { MATCH_STR_1, MapString(arg1), mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, record.arg1);
	}

	void WriteMatch(const char *arg1, int arg2)
	{
#pragma pack(push, 1)
		struct { char op; int arg1, arg2; char sync; } record = { MATCH_STR_1|MATCH_INT_2, MapString(arg1), arg2, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, record.arg1 ^ record.arg2);
	}

	void WriteMatch(const char *arg1, const char *arg2)
	{
#pragma pack(push, 1)
		struct { char op; int arg1, arg2; char sync; } record = { MATCH_STR_1|MATCH_STR_2, MapString(arg1), MapString(arg2), mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, record.arg1 ^ record.arg2);
	}

	void WriteMatch(const char *arg1, int arg2, int arg3)
	{
#pragma pack(push, 1)
		struct { char op; int arg1, arg2, arg3; char sync; } record = { MATCH_STR_1|MATCH_INT_2|MATCH_INT_3, MapString(arg1), arg2, arg3, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, record.arg1 ^ record.arg2);
	}

	void WriteCmd(char cmd, int arg1)
	{
		QCHECK((cmd & MATCH_ESCAPE) == 0);

#pragma pack(push, 1)
		struct { char op; int arg1; char sync; } record = { MATCH_ESCAPE|cmd, arg1, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, record.arg1);
	}

	void WriteCmd(char cmd, const char *arg1, int arg2)
	{
		QCHECK((cmd & MATCH_ESCAPE) == 0);

#pragma pack(push, 1)
		struct { char op; int arg1, arg2; char sync; } record = { MATCH_ESCAPE|cmd, MapString(arg1), arg2, mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, record.arg1 ^ record.arg2);
	}
	
	void WriteCmd(char cmd, const void *handle, const char *arg1)
	{
		QCHECK((cmd & MATCH_ESCAPE) == 0);

#pragma pack(push, 1)
		struct { char op; int handle, arg1; char sync; } record = { MATCH_ESCAPE|cmd, reinterpret_cast<int>(handle), MapString(arg1), mSyncCount++ };
#pragma pack(pop)

		mPipe.Write(&record, sizeof(record));
		mPipe.WaitForAck(record.op, record.arg1);
	}

	void WaitForAck(char cmd, int key)
	{
		mPipe.WaitForAck(cmd, key);
	}
};

void TraceInit(const char *id)
{
	QCHECK(mSession == NULL && mContext == NULL);
	mSession = new TraceSession(id);
	mContext = new TraceContext();
}

void TraceStop()
{
	QCHECK(mSession != NULL && mContext != NULL);

	delete mContext;
	mContext = NULL;

	delete mSession;
	mSession = NULL;
}

void* GetRootContext()
{
	QCHECK(mSession != NULL);
	return mSession;
}

void TraceInitThread(void *root)
{
	QCHECK(mSession == NULL && mContext == NULL);
	mSession = static_cast<TraceSession*>(root);
	mContext = new TraceContext();
	SetThreadName(-1, "LZMA Native Thread");
}

void TraceStopThread()
{
	QCHECK(mSession != NULL && mContext != NULL);
	delete mContext;
	mContext = NULL;
	mSession = NULL;
}

void TR(const char *key, int value)
{
	QCHECK(value != 0xcdcdcdcd);
	mContext->WriteMatch(key, value);
}

void TRS(const char *key, const char* value)
{
	mContext->WriteMatch(key, value);
}

void TRI(int value1, int value2)
{
	mContext->WriteMatch(value1, value2);
}

void TRZC(void *handle)
{
	int threadId = GetThreadId(handle);
	QCHECK(threadId != 0);
	mContext->WriteCmd(CMD_THREAD_CTOR, threadId);
}

void TRZD(void *handle)
{
	int threadId = GetThreadId(handle);
	QCHECK(threadId != 0);
	mContext->WriteCmd(CMD_THREAD_DTOR, threadId);
}

void TRZ1(void *handle)
{
	int threadId = GetThreadId(handle);
	QCHECK(threadId != 0);
	mContext->WriteCmd(CMD_THREAD_WAIT, threadId);
}

int TSZ(const char *key, int res)
{
	TraceStatusCode(key, res);
	return res;
}

void TraceObjectCreate(const char *key, void *handle)
{
	QCHECK(handle != NULL && (int)handle != 0xcdcdcdcd);
	mContext->WriteCmd(CMD_OBJECT_CTOR, handle, key);
}

void TraceObjectDelete(const char *key, void *handle)
{
	if(handle == NULL) return;
	QCHECK((int)handle != 0xcdcdcdcd);
	int arg1 = mSession->WriteCmd(CMD_OBJECT_DTOR, handle, key);
	mContext->WaitForAck(CMD_OBJECT_DTOR, arg1);
}

void TraceObjectSync(const char *key, void *handle)
{
	QCHECK(handle != NULL && (int)handle != 0xcdcdcdcd);
	int arg1 = mSession->WriteCmd(CMD_OBJECT_WAIT1, handle, key);
	mContext->WaitForAck(CMD_OBJECT_WAIT1, arg1);
}

void TraceStatusCode(const char *key, unsigned long res)
{
	QCHECK((0 <= res && res <= 12) || res == 16 || res == 17);
	mContext->WriteCmd(CMD_STATUS_CODE, key, res);
}

void EnterGlobalLock()
{
	mSession->Lock();
}

void LeaveGlobalLock()
{
	mSession->Unlock();
}
