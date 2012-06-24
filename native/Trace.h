#pragma once

#ifdef __cplusplus
extern "C" {
#endif

void TraceInit(const char *id);
void TraceStop();
void* GetRootContext();
void TraceInitThread(void *root);
void TraceStopThread();
void TR(const char *key, int value);
void TRS(const char *key, const char *value);
void TRI(int value1, int value2);
void TRZC(void *handle);
void TRZD(void *handle);
void TRZ1(void *handle);
int TSZ(const char* key, int res);

void TraceObjectCreate(const char *key, void *handle);
void TraceObjectDelete(const char *key, void *handle);
void TraceObjectSync(const char *key, void *handle);
void TraceStatusCode(const char *key, unsigned long res);

void EnterGlobalLock();
void LeaveGlobalLock();

#ifdef __cplusplus
}
#endif
