/* MtCoder.c -- Multi-thread Coder
2010-09-24 : Igor Pavlov : Public domain */

#include <stdio.h>

#include "MtCoder.h"

void LoopThread_Construct(CLoopThread *p)
{
  Thread_Construct(&p->thread);
  Event_Construct(&p->startEvent);
  Event_Construct(&p->finishedEvent);
}

void LoopThread_Close(CLoopThread *p)
{
  Thread_Close(&p->thread);
  Event_Close(&p->startEvent);
  Event_Close(&p->finishedEvent);
}

static THREAD_FUNC_RET_TYPE THREAD_FUNC_CALL_TYPE LoopThreadFunc(void *pp)
{
  CLoopThread *p = (CLoopThread *)pp;
  for (;;)
  {
    if (Event_Wait(&p->startEvent) != 0)
      return SZ_ERROR_THREAD;
    EnterGlobalLock();
    TraceObjectSync("LoopThreadFunc", p);
    if (p->stop) {
      TraceObjectDelete("LoopThreadFunc", p);
      LeaveGlobalLock();
      return 0;
    }
    TraceObjectSync("LoopThreadFunc", p);
    LeaveGlobalLock();
    p->res = p->func(p->param);
    if (Event_Set(&p->finishedEvent) != 0)
      return SZ_ERROR_THREAD;
  }
}

WRes LoopThread_Create(CLoopThread *p)
{
  EnterGlobalLock();
  p->stop = 0;
  TraceObjectCreate("LoopThread_Create", p);
  TraceObjectSync("LoopThread_Create", p);
  LeaveGlobalLock();
  RINOK(AutoResetEvent_CreateNotSignaled(&p->startEvent));
  RINOK(AutoResetEvent_CreateNotSignaled(&p->finishedEvent));
  return Thread_Create(&p->thread, LoopThreadFunc, p);
}

WRes LoopThread_StopAndWait(CLoopThread *p)
{
  EnterGlobalLock();
  p->stop = 1;
  TraceObjectSync("LoopThread_StopAndWait", p);
  TraceObjectSync("LoopThread_StopAndWait", p);
  LeaveGlobalLock();
  if (Event_Set(&p->startEvent) != 0)
    return SZ_ERROR_THREAD;
  return Thread_Wait(&p->thread);
}

WRes LoopThread_StartSubThread(CLoopThread *p) { return Event_Set(&p->startEvent); }
WRes LoopThread_WaitSubThread(CLoopThread *p) { return Event_Wait(&p->finishedEvent); }

static SRes Progress(ICompressProgress *p, UInt64 inSize, UInt64 outSize)
{
  return (p && p->Progress(p, inSize, outSize) != SZ_OK) ? SZ_ERROR_PROGRESS : SZ_OK;
}

static void MtProgress_Init(CMtProgress *p, ICompressProgress *progress)
{
  unsigned i;
  for (i = 0; i < NUM_MT_CODER_THREADS_MAX; i++)
    p->inSizes[i] = p->outSizes[i] = 0;
  p->totalInSize = p->totalOutSize = 0;
  p->progress = progress;
  p->res = SZ_OK;
}

static void MtProgress_Reinit(CMtProgress *p, unsigned index)
{
  p->inSizes[index] = 0;
  p->outSizes[index] = 0;
}

#define UPDATE_PROGRESS(size, prev, total) \
  if (size != (UInt64)(Int64)-1) { total += size - prev; prev = size; }

SRes MtProgress_Set(CMtProgress *p, unsigned index, UInt64 inSize, UInt64 outSize)
{
  SRes res;
  CriticalSection_Enter(&p->cs);
  UPDATE_PROGRESS(inSize, p->inSizes[index], p->totalInSize)
  UPDATE_PROGRESS(outSize, p->outSizes[index], p->totalOutSize)
  if (p->res == SZ_OK)
    p->res = Progress(p->progress, p->totalInSize, p->totalOutSize);
  res = p->res;
  CriticalSection_Leave(&p->cs);
  return res;
}

static void MtProgress_SetError(CMtProgress *p, SRes res)
{
  CriticalSection_Enter(&p->cs);
  if (p->res == SZ_OK)
    p->res = res;
  CriticalSection_Leave(&p->cs);
}

static void MtCoder_SetError(CMtCoder* p, SRes res)
{
  CriticalSection_Enter(&p->cs);
  if (p->res == SZ_OK)
    p->res = res;
  CriticalSection_Leave(&p->cs);
}

/* ---------- MtThread ---------- */

void CMtThread_Construct(CMtThread *p, CMtCoder *mtCoder)
{
  TraceObjectCreate("CMtThread_Construct",p);
  p->mtCoder = mtCoder;
  p->outBuf = 0;
  p->inBuf = 0;
  Event_Construct(&p->canRead);
  Event_Construct(&p->canWrite);
  LoopThread_Construct(&p->thread);
}

#define RINOK_THREAD(x) { if((x) != 0) return SZ_ERROR_THREAD; }

static void CMtThread_CloseEvents(CMtThread *p)
{
  Event_Close(&p->canRead);
  Event_Close(&p->canWrite);
}

static void CMtThread_Destruct(CMtThread *p)
{
  TraceObjectDelete("CMtThread_Destruct",p);
  CMtThread_CloseEvents(p);

  if (Thread_WasCreated(&p->thread.thread))
  {
    LoopThread_StopAndWait(&p->thread);
    LoopThread_Close(&p->thread);
  }

  if (p->mtCoder->alloc)
    IAlloc_Free(p->mtCoder->alloc, p->outBuf);
  p->outBuf = 0;

  if (p->mtCoder->alloc)
    IAlloc_Free(p->mtCoder->alloc, p->inBuf);
  p->inBuf = 0;
}

#define MY_BUF_ALLOC(buf, size, newSize) \
  if (buf == 0 || size != newSize) \
  { IAlloc_Free(p->mtCoder->alloc, buf); \
    size = newSize; buf = (Byte *)IAlloc_Alloc(p->mtCoder->alloc, size); \
    if (buf == 0) return SZ_ERROR_MEM; }

static SRes CMtThread_Prepare(CMtThread *p)
{
  MY_BUF_ALLOC(p->inBuf, p->inBufSize, p->mtCoder->blockSize)
  MY_BUF_ALLOC(p->outBuf, p->outBufSize, p->mtCoder->destBlockSize)

  EnterGlobalLock();
  TraceObjectSync("CMtThread_Prepare", p);
  p->stopReading = False;
  p->stopWriting = False;
  TraceObjectSync("CMtThread_Prepare", p);
  LeaveGlobalLock();
  RINOK_THREAD(AutoResetEvent_CreateNotSignaled(&p->canRead));
  RINOK_THREAD(AutoResetEvent_CreateNotSignaled(&p->canWrite));

  return SZ_OK;
}

static SRes FullRead(ISeqInStream *stream, Byte *data, size_t *processedSize)
{
  size_t size = *processedSize;
  *processedSize = 0;
  while (size != 0)
  {
    size_t curSize = size;
    SRes res = stream->Read(stream, data, &curSize);
    *processedSize += curSize;
    data += curSize;
    size -= curSize;
    RINOK(res);
    if (curSize == 0)
      return SZ_OK;
  }
  return SZ_OK;
}

#define GET_NEXT_THREAD(p) &p->mtCoder->threads[p->index == p->mtCoder->numThreads  - 1 ? 0 : p->index + 1]

static SRes MtThread_Process(CMtThread *p, Bool *stop)
{
  CMtThread *next;
  *stop = True;
  if (Event_Wait(&p->canRead) != 0)
    return SZ_ERROR_THREAD;
  
  next = GET_NEXT_THREAD(p);
  
  EnterGlobalLock();
  TraceObjectSync("MtThread_Process", p);
  if (p->stopReading)
  {
    next->stopReading = True;
    TraceObjectSync("MtThread_Process", p);
    LeaveGlobalLock();
    return Event_Set(&next->canRead) == 0 ? SZ_OK : SZ_ERROR_THREAD;
  }
  TraceObjectSync("MtThread_Process", p);
  LeaveGlobalLock();

  {
    size_t size = p->mtCoder->blockSize;
    size_t destSize = p->outBufSize;

    RINOK(FullRead(p->mtCoder->inStream, p->inBuf, &size));
    EnterGlobalLock();
    TraceObjectSync("MtThread_Process:2", p);
    next->stopReading = *stop = (size != p->mtCoder->blockSize);
    TraceObjectSync("MtThread_Process:2", p);
    LeaveGlobalLock();
    if (Event_Set(&next->canRead) != 0)
      return SZ_ERROR_THREAD;

    RINOK(p->mtCoder->mtCallback->Code(p->mtCoder->mtCallback, p->index,
        p->outBuf, &destSize, p->inBuf, size, *stop));

    MtProgress_Reinit(&p->mtCoder->mtProgress, p->index);

    if (Event_Wait(&p->canWrite) != 0)
      return SZ_ERROR_THREAD;
    EnterGlobalLock();
    TraceObjectSync("MtThread_Process:3", p);
    if (p->stopWriting) {
      TraceObjectSync("MtThread_Process:3", p);
      LeaveGlobalLock();
      return SZ_ERROR_FAIL;
    }
    TraceObjectSync("MtThread_Process:3", p);
    LeaveGlobalLock();
    if (p->mtCoder->outStream->Write(p->mtCoder->outStream, p->outBuf, destSize) != destSize)
      return SZ_ERROR_WRITE;
    return Event_Set(&next->canWrite) == 0 ? SZ_OK : SZ_ERROR_THREAD;
  }
}

static THREAD_FUNC_RET_TYPE THREAD_FUNC_CALL_TYPE ThreadFunc(void *pp)
{
  CMtThread *p = (CMtThread *)pp;
  for (;;)
  {
    Bool stop;
    CMtThread *next = GET_NEXT_THREAD(p);
    SRes res = MtThread_Process(p, &stop);
    if (res != SZ_OK)
    {
      MtCoder_SetError(p->mtCoder, res);
      MtProgress_SetError(&p->mtCoder->mtProgress, res);
      EnterGlobalLock();
      TraceObjectSync("ThreadFunc", p);
      next->stopReading = True;
      next->stopWriting = True;
      TraceObjectSync("ThreadFunc", p);
      LeaveGlobalLock();
      Event_Set(&next->canRead);
      Event_Set(&next->canWrite);
      return res;
    }
    if (stop)
      return 0;
  }
}

void MtCoder_Construct(CMtCoder* p)
{
  unsigned i;
  p->alloc = 0;
  for (i = 0; i < NUM_MT_CODER_THREADS_MAX; i++)
  {
    CMtThread *t = &p->threads[i];
    t->index = i;
    CMtThread_Construct(t, p);
  }
  CriticalSection_Init(&p->cs);
  CriticalSection_Init(&p->mtProgress.cs);
}

void MtCoder_Destruct(CMtCoder* p)
{
  unsigned i;
  for (i = 0; i < NUM_MT_CODER_THREADS_MAX; i++)
    CMtThread_Destruct(&p->threads[i]);
  CriticalSection_Delete(&p->cs);
  CriticalSection_Delete(&p->mtProgress.cs);
}

SRes MtCoder_Code(CMtCoder *p)
{
  unsigned i, numThreads = p->numThreads;
  SRes res = SZ_OK;
  p->res = SZ_OK;

  MtProgress_Init(&p->mtProgress, p->progress);

  for (i = 0; i < numThreads; i++)
  {
    RINOK(CMtThread_Prepare(&p->threads[i]));
  }

  for (i = 0; i < numThreads; i++)
  {
    CMtThread *t = &p->threads[i];
    CLoopThread *lt = &t->thread;

    if (!Thread_WasCreated(&lt->thread))
    {
      lt->func = ThreadFunc;
      lt->param = t;

      if (LoopThread_Create(lt) != SZ_OK)
      {
        res = SZ_ERROR_THREAD;
        break;
      }
    }
  }

  if (res == SZ_OK)
  {
    unsigned j;
    for (i = 0; i < numThreads; i++)
    {
      CMtThread *t = &p->threads[i];
      if (LoopThread_StartSubThread(&t->thread) != SZ_OK)
      {
        res = SZ_ERROR_THREAD;
        EnterGlobalLock();
        TraceObjectSync("MtCoder_Code", p);
        p->threads[0].stopReading = True;
        TraceObjectSync("MtCoder_Code", p);
        LeaveGlobalLock();
        break;
      }
    }

    Event_Set(&p->threads[0].canWrite);
    Event_Set(&p->threads[0].canRead);

    for (j = 0; j < i; j++)
      LoopThread_WaitSubThread(&p->threads[j].thread);
  }

  for (i = 0; i < numThreads; i++)
    CMtThread_CloseEvents(&p->threads[i]);
  return (res == SZ_OK) ? p->res : res;
}
