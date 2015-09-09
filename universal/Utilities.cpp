#include "pch.h"
#include "Utilities.h"

using namespace Platform;

Exception^ CreateException(SRes res)
{
	switch (res)
	{
	case SZ_ERROR_MEM:
		return ref new OutOfMemoryException();

	case SZ_ERROR_PARAM:
		return ref new InvalidArgumentException();

	case SZ_OK:
	case SZ_ERROR_DATA:
	case SZ_ERROR_CRC:
	case SZ_ERROR_UNSUPPORTED:
	case SZ_ERROR_INPUT_EOF:
	case SZ_ERROR_OUTPUT_EOF:
	case SZ_ERROR_READ:
	case SZ_ERROR_WRITE:
	case SZ_ERROR_PROGRESS:
	case SZ_ERROR_FAIL:
	case SZ_ERROR_THREAD:
	case SZ_ERROR_ARCHIVE:
	case SZ_ERROR_NO_ARCHIVE:
	default:
		return ref new FailureException();
	}
}

static void* AllocImpl(void *p, size_t size)
{
	return malloc(size);
}

static void FreeImpl(void *p, void *address)
{
	// address can be 0
	if (address)
		free(address);
}

static ISzAlloc kAllocImplDef = { &AllocImpl, &FreeImpl };
ISzAlloc* kAllocImpl = &kAllocImplDef;
