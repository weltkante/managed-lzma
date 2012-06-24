/* Threads.c -- multithreading library
2009-09-20 : Igor Pavlov : Public domain */

#ifndef _WIN32_WCE
#include <process.h>
#endif

#include "Threads.h"

static WRes GetError()
{
  DWORD res = GetLastError();
  return (res) ? (WRes)(res) : 1;
}

WRes HandleToWRes(HANDLE h) { return (h != 0) ? 0 : GetError(); }
WRes BOOLToWRes(BOOL v) { return v ? 0 : GetError(); }

WRes HandlePtr_Close(HANDLE *p)
{
  if (*p != NULL)
    if (!CloseHandle(*p))
      return GetError();
  *p = NULL;
  return 0;
}

WRes Handle_WaitObject(HANDLE h) {
  return (WRes)WaitForSingleObject(h, INFINITE);
}

typedef struct TC
{
	THREAD_FUNC_TYPE func;
	LPVOID param;
	LPVOID master;
} TC;

static THREAD_FUNC_RET_TYPE THREAD_FUNC_CALL_TYPE Thread_Stub(void *param)
{
	TC *context = (TC*)param;
	THREAD_FUNC_RET_TYPE result;
	TraceInitThread(context->master);
	result = context->func(context->param);
	TraceStopThread();
	free(context);
	return result;
}

WRes Thread_Create(CThread *p, THREAD_FUNC_TYPE func, LPVOID param)
{
  unsigned threadId; /* Windows Me/98/95: threadId parameter may not be NULL in _beginthreadex/CreateThread functions */
  TC *ctx = (TC*)malloc(sizeof(TC));
  ctx->func = func;
  ctx->param = param;
  ctx->master = GetRootContext();
  param = ctx;
  *p =
    #ifdef UNDER_CE
    CreateThread(0, 0, func, param, 0, &threadId);
    #else
    (HANDLE)_beginthreadex(NULL, 0, Thread_Stub, param, 0, &threadId);
    #endif
    /* maybe we must use errno here, but probably GetLastError() is also OK. */
  TRZC(*p);
  return TSZ("Thread_Create", HandleToWRes(*p));
}

void Thread_Close(CThread *p)
{
  HANDLE h = *p;
  TRZD(h); // must be done before close, because then the handle is invalid
  HandlePtr_Close(p);
}

WRes Thread_Wait(CThread *p)
{
  WRes res;
  res = Handle_WaitObject(*p);
  TRZ1(*p);
  return TSZ("Thread_Wait",res);
}

WRes AutoResetEvent_CreateNotSignaled(CAutoResetEvent *p)
{
  WRes res;
  *p = CreateEvent(NULL, FALSE, FALSE, NULL);
  TraceObjectCreate("Event_Create", *p);
  res = HandleToWRes(*p);
  TraceStatusCode("Event_Create", res);
  return res;
}

void Event_Close(CEvent *p)
{
  HANDLE h = *p;
  HandlePtr_Close(p);
  TraceObjectDelete("Event_Close",h);
}

WRes Event_Wait(CEvent *p)
{
  WRes res;
  res = Handle_WaitObject(*p);
  TraceObjectSync("Event_Wait",*p);
  TraceStatusCode("Event_Wait",res);
  return res;
}

WRes Event_Set(CEvent *p)
{
  WRes res;
  TraceObjectSync("Event_Set",*p);
  res = BOOLToWRes(SetEvent(*p));
  TraceStatusCode("Event_Set",res);
  return res;
}

WRes Event_Reset(CEvent *p)
{
  WRes res;
  TraceObjectSync("Event_Reset",*p);
  res = BOOLToWRes(ResetEvent(*p));
  TraceStatusCode("Event_Reset",res);
  return res;
}

WRes Semaphore_Create(CSemaphore *p, UInt32 initCount, UInt32 maxCount)
{
  *p = CreateSemaphore(NULL, (LONG)initCount, (LONG)maxCount, NULL);
  TraceObjectCreate("Semaphore_Create",*p);
  TRI(initCount, maxCount);
  return TSZ("Semaphore_Create",HandleToWRes(*p));
}

void Semaphore_Close(CSemaphore *p)
{
  TraceObjectDelete("Semaphore_Close",*p);
  HandlePtr_Close(p);
}

WRes Semaphore_Release1(CSemaphore *p)
{
  // Sync must happen before release, otherwise we have a race condition!
  // Another thread may aquire the semaphore and write to the sync channel before we did!
  WRes res;
  TraceObjectSync("Semaphore_Release",*p);
  res = BOOLToWRes(ReleaseSemaphore(*p, 1, NULL));
  return TSZ("Semaphore_Release",res);
}

WRes Semaphore_Wait(CSemaphore *p)
{
  // Sync must happen after wait, otherwise we have a race condition!
  // Another thread may pass through the wait before we do and we would have synced a wrong order!
  WRes res = Handle_WaitObject(*p);
  TraceObjectSync("Semaphore_Wait",*p);
  return TSZ("Semaphore_Wait",res);
}

WRes CriticalSection_Init(CCriticalSection *p)
{
  /* InitializeCriticalSection can raise only STATUS_NO_MEMORY exception */
  #ifdef _MSC_VER
  __try
  #endif
  {
    InitializeCriticalSection(p);
    /* InitializeCriticalSectionAndSpinCount(p, 0); */
  }
  #ifdef _MSC_VER
  __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
  #endif
  TraceObjectCreate("CriticalSection_Init",p);
  return 0;
}

void CriticalSection_Delete(CCriticalSection *p)
{
  TraceObjectDelete("CriticalSection_Delete",p);
  DeleteCriticalSection(p);
}

void CriticalSection_Enter(CCriticalSection *p)
{
  EnterCriticalSection(p);
  TraceObjectSync("CriticalSection_Enter",p);
}

void CriticalSection_Leave(CCriticalSection *p)
{
  TraceObjectSync("CriticalSection_Leave",p);
  LeaveCriticalSection(p);
}
