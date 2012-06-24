/* Threads.h -- multithreading library
2009-03-27 : Igor Pavlov : Public domain */

#ifndef __7Z_THREADS_H
#define __7Z_THREADS_H

#include "Types.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef HANDLE CThread;
#define Thread_Construct(p) *(p) = NULL
#define Thread_WasCreated(p) (*(p) != NULL)
void Thread_Close(CThread *p);
WRes Thread_Wait(CThread *p);
typedef unsigned THREAD_FUNC_RET_TYPE;
#define THREAD_FUNC_CALL_TYPE MY_STD_CALL
#define THREAD_FUNC_DECL THREAD_FUNC_RET_TYPE THREAD_FUNC_CALL_TYPE
typedef THREAD_FUNC_RET_TYPE (THREAD_FUNC_CALL_TYPE * THREAD_FUNC_TYPE)(void *);
WRes Thread_Create(CThread *p, THREAD_FUNC_TYPE func, LPVOID param);

typedef HANDLE CEvent;
typedef CEvent CAutoResetEvent;
#define Event_Construct(p) *(p) = NULL
#define Event_IsCreated(p) (*(p) != NULL)
void Event_Close(CEvent *p);
WRes Event_Wait(CEvent *p);
WRes Event_Set(CEvent *p);
WRes Event_Reset(CEvent *p);
WRes AutoResetEvent_CreateNotSignaled(CAutoResetEvent *p);

typedef HANDLE CSemaphore;
#define Semaphore_Construct(p) (*p) = NULL
void Semaphore_Close(CSemaphore *p);
WRes Semaphore_Wait(CSemaphore *p);
WRes Semaphore_Create(CSemaphore *p, UInt32 initCount, UInt32 maxCount);
WRes Semaphore_Release1(CSemaphore *p);

typedef CRITICAL_SECTION CCriticalSection;
WRes CriticalSection_Init(CCriticalSection *p);
void CriticalSection_Delete(CCriticalSection *p);
void CriticalSection_Enter(CCriticalSection *p);
void CriticalSection_Leave(CCriticalSection *p);

#ifdef __cplusplus
}
#endif

#endif
