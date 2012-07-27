using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ManagedLzma.LZMA.Master
{
    partial class LZMA
    {
        internal const int kMtHashBlockSize = (1 << 13);
        internal const int kMtHashNumBlocks = (1 << 3);
        internal const int kMtHashNumBlocksMask = (kMtHashNumBlocks - 1);

        internal const int kMtBtBlockSize = (1 << 14);
        internal const int kMtBtNumBlocks = (1 << 6);
        internal const int kMtBtNumBlocksMask = (kMtBtNumBlocks - 1);

        private const uint kMtMaxValForNormalize = 0xFFFFFFFF;
        private const int kEmptyHashValue = 0;

        private const int kHashBufferSize = (kMtHashBlockSize * kMtHashNumBlocks);
        private const int kBtBufferSize = (kMtBtBlockSize * kMtBtNumBlocks);

        internal sealed class CMtSync
        {
            #region Variables

            internal bool mWasCreated;
            internal bool mNeedStart;
            internal bool mExit;
            internal bool mStopWriting;

            internal CThread mThread;
            internal CEvent mCanStart;
            internal CEvent mWasStarted;
            internal CEvent mWasStopped;
            internal CSemaphore mFreeSemaphore;
            internal CSemaphore mFilledSemaphore;
            internal bool mCsWasInitialized;
            internal bool mCsWasEntered;
            internal CCriticalSection mCS;
            internal uint mNumProcessedBlocks;

            #endregion

            #region Methods

            internal void MtSync_Construct()
            {
                mWasCreated = false;
                mCsWasInitialized = false;
                mCsWasEntered = false;
                Thread_Construct(out mThread);
                Event_Construct(out mCanStart);
                Event_Construct(out mWasStarted);
                Event_Construct(out mWasStopped);
                Semaphore_Construct(out mFreeSemaphore);
                Semaphore_Construct(out mFilledSemaphore);
            }

            internal void MtSync_GetNextBlock()
            {
                if(mNeedStart)
                {
                    mNumProcessedBlocks = 1;
                    mNeedStart = false;
                    if(SyncTrace.Enable)
                        Trace.MatchObjectWait(this, "MtSync_GetNextBlock:start");
                    mStopWriting = false;
                    if(SyncTrace.Enable)
                        Trace.MatchObjectWait(this, "MtSync_GetNextBlock:start");
                    mExit = false;
                    Event_Reset(mWasStarted);
                    Event_Reset(mWasStopped);
                    Event_Set(mCanStart);
                    Event_Wait(mWasStarted);
                }
                else
                {
                    CriticalSection_Leave(mCS);
                    mCsWasEntered = false;
                    mNumProcessedBlocks++;
                    Semaphore_Release1(mFreeSemaphore);
                }
                Semaphore_Wait(mFilledSemaphore);
                CriticalSection_Enter(mCS);
                mCsWasEntered = true;
            }

            /* MtSync_StopWriting must be called if Writing was started */

            internal void MtSync_StopWriting()
            {
                uint myNumBlocks = mNumProcessedBlocks;
                if(!Thread_WasCreated(mThread) || mNeedStart)
                    return;

                if(SyncTrace.Enable)
                    Trace.MatchObjectWait(this, "MtSync_StopWriting:stop");
                mStopWriting = true;
                if(SyncTrace.Enable)
                    Trace.MatchObjectWait(this, "MtSync_StopWriting:stop");

                if(mCsWasEntered)
                {
                    CriticalSection_Leave(mCS);
                    mCsWasEntered = false;
                }

                Semaphore_Release1(mFreeSemaphore);

                Event_Wait(mWasStopped);

                while(myNumBlocks++ != mNumProcessedBlocks)
                {
                    Semaphore_Wait(mFilledSemaphore);
                    Semaphore_Release1(mFreeSemaphore);
                }

                mNeedStart = true;
            }

            internal void MtSync_Destruct()
            {
                if(Thread_WasCreated(mThread))
                {
                    MtSync_StopWriting();
                    mExit = true;
                    if(mNeedStart)
                        Event_Set(mCanStart);
                    Thread_Wait(mThread);
                    Thread_Close(ref mThread);
                }

                if(mCsWasInitialized)
                {
                    CriticalSection_Delete(mCS);
                    mCsWasInitialized = false;
                }

                Event_Close(ref mCanStart);
                Event_Close(ref mWasStarted);
                Event_Close(ref mWasStopped);
                Semaphore_Close(ref mFreeSemaphore);
                Semaphore_Close(ref mFilledSemaphore);

                if(mWasCreated)
                {
                    if(SyncTrace.Enable)
                        Trace.MatchObjectDestroy(this, "MtSync_Destruct");
                }

                mWasCreated = false;
            }

            private SRes MtSync_Create2(Action startAddress, uint numBlocks)
            {
                if(mWasCreated)
                    return SZ_OK;

                if(SyncTrace.Enable)
                    Trace.MatchObjectCreate(this, "MtSync_Create2");

                if(CriticalSection_Init(out mCS) != SZ_OK)
                    return SZ_ERROR_THREAD;
                mCsWasInitialized = true;

                if(AutoResetEvent_CreateNotSignaled(out mCanStart) != SZ_OK)
                    return SZ_ERROR_THREAD;
                if(AutoResetEvent_CreateNotSignaled(out mWasStarted) != SZ_OK)
                    return SZ_ERROR_THREAD;
                if(AutoResetEvent_CreateNotSignaled(out mWasStopped) != SZ_OK)
                    return SZ_ERROR_THREAD;

                if(Semaphore_Create(out mFreeSemaphore, numBlocks, numBlocks) != SZ_OK)
                    return SZ_ERROR_THREAD;
                if(Semaphore_Create(out mFilledSemaphore, 0, numBlocks) != SZ_OK)
                    return SZ_ERROR_THREAD;

                mNeedStart = true;

                if(Thread_Create(out mThread, startAddress) != SZ_OK)
                    return SZ_ERROR_THREAD;
                mWasCreated = true;

                return SZ_OK;
            }

            internal SRes MtSync_Create(Action startAddress, uint numBlocks)
            {
                SRes res = MtSync_Create2(startAddress, numBlocks);
                if(res != SZ_OK)
                    MtSync_Destruct();
                return res;
            }

            #endregion
        }

        internal interface IMatchFinderMt: IMatchFinder
        {
            /* LZ */
            P<uint> MixMatchesFunc(object p, uint matchMinPos, P<uint> distances);

            /* Hash */
            void GetHeadsFunc(P<byte> buffer, uint pos, P<uint> hash, uint hashMask, P<uint> heads, uint numHeads);
        }

        internal class CMatchFinderMt
        {
            #region Variables

            internal IMatchFinderMt mInterface;

            /* LZ */
            internal P<byte> mPointerToCurPos;
            internal P<uint> mBtBuf;
            internal uint mBtBufPos;
            internal uint mBtBufPosLimit;
            internal uint mLzPos;
            internal uint mBtNumAvailBytes;

            internal uint[] mHash;
            internal uint mFixedHashSize;
            internal uint mHistorySize;

            /* LZ + BT */
            internal CMtSync mBtSync = new CMtSync();

            /* BT */
            internal uint[] mHashBuf;
            internal uint mHashBufPos;
            internal uint mHashBufPosLimit;
            internal uint mHashNumAvail;

            internal P<uint> mSon;
            internal uint mMatchMaxLen;
            internal uint mNumHashBytes;
            internal uint mPos;
            internal P<byte> mBuffer;
            internal uint mCyclicBufferPos;
            internal uint mCyclicBufferSize; // it must be historySize + 1
            internal uint mCutValue;

            /* BT + Hash */
            internal CMtSync mHashSync = new CMtSync();

            /* Hash */
            internal CMatchFinder @base;

            #endregion

            #region Methods

            internal CMatchFinderMt()
            {
                @base = new CMatchFinder();

                TR("MatchFinderMt_Construct", 0);

                mHashBuf = null;
                mHashSync.MtSync_Construct();
                mBtSync.MtSync_Construct();
            }

            internal void MatchFinderMt_GetNextBlock_Hash()
            {
                mHashSync.MtSync_GetNextBlock();
                mHashBufPosLimit = mHashBufPos = ((mHashSync.mNumProcessedBlocks - 1) & kMtHashNumBlocksMask) * kMtHashBlockSize;
                mHashBufPosLimit += mHashBuf[mHashBufPos++];
                mHashNumAvail = mHashBuf[mHashBufPos++];
            }

            internal void MatchFinderMt_Destruct(ISzAlloc alloc)
            {
                mHashSync.MtSync_Destruct();
                mBtSync.MtSync_Destruct();
                alloc.Free(alloc, mHashBuf);
                mHashBuf = null;
            }

            /* ReleaseStream is required to finish multithreading */
            internal void MatchFinderMt_ReleaseStream()
            {
                mBtSync.MtSync_StopWriting();
            }

            #endregion

            private void MatchFinderMt_Normalize()
            {
                CMatchFinder.MatchFinder_Normalize3(mLzPos - mHistorySize - 1, mHash, mFixedHashSize);
                mLzPos = mHistorySize + 1;
            }

            internal void MatchFinderMt_GetNextBlock_Bt()
            {
                mBtSync.MtSync_GetNextBlock();
                uint blockIndex = (mBtSync.mNumProcessedBlocks - 1) & kMtBtNumBlocksMask;
                mBtBufPos = blockIndex * kMtBtBlockSize;
                mBtBufPosLimit = mBtBufPos;
                mBtBufPosLimit += mBtBuf[mBtBufPos++];
                mBtNumAvailBytes = mBtBuf[mBtBufPos++];
                if(mLzPos >= kMtMaxValForNormalize - kMtBtBlockSize)
                    MatchFinderMt_Normalize();
            }

            internal P<byte> MatchFinderMt_GetPointerToCurrentPos()
            {
                return mPointerToCurPos;
            }

            internal uint MatchFinderMt_GetNumAvailableBytes()
            {
                TR("MatchFinderMt_GetNumAvailableBytes", mBtBufPos);
                if(mBtBufPos == mBtBufPosLimit)
                    MatchFinderMt_GetNextBlock_Bt();

                TR("MatchFinderMt_GetNumAvailableBytes", mBtNumAvailBytes);
                return mBtNumAvailBytes;
            }

            internal byte MatchFinderMt_GetIndexByte(int index)
            {
                return mPointerToCurPos[index];
            }

            #region More Methods. Not sure if the are right in this class.

            internal void HashThreadFunc()
            {
                CMatchFinderMt mt = this;
                CMtSync p = mt.mHashSync;
                for(; ; )
                {
                    uint numProcessedBlocks = 0;
                    Event_Wait(p.mCanStart);
                    Event_Set(p.mWasStarted);
                    for(; ; )
                    {
                        if(p.mExit)
                            return;

                        if(SyncTrace.Enable)
                            Trace.MatchObjectWait(p, "HashThreadFunc:stop");
                        if(p.mStopWriting)
                        {
                            if(SyncTrace.Enable)
                                Trace.MatchObjectWait(p, "HashThreadFunc:stop");
                            p.mNumProcessedBlocks = numProcessedBlocks;
                            Event_Set(p.mWasStopped);
                            break;
                        }
                        if(SyncTrace.Enable)
                            Trace.MatchObjectWait(p, "HashThreadFunc:stop");

                        {
                            CMatchFinder mf = mt.@base;
                            if(mf.MatchFinder_NeedMove())
                            {
                                CriticalSection_Enter(mt.mBtSync.mCS);
                                CriticalSection_Enter(mt.mHashSync.mCS);
                                {
                                    P<byte> beforePtr = mf.MatchFinder_GetPointerToCurrentPos();
                                    mf.MatchFinder_MoveBlock();
                                    P<byte> afterPtr = mf.MatchFinder_GetPointerToCurrentPos();
                                    mt.mPointerToCurPos -= beforePtr - afterPtr;
                                    mt.mBuffer -= beforePtr - afterPtr;
                                }
                                CriticalSection_Leave(mt.mBtSync.mCS);
                                CriticalSection_Leave(mt.mHashSync.mCS);
                                continue;
                            }

                            Semaphore_Wait(p.mFreeSemaphore);

                            mf.MatchFinder_ReadIfRequired();
                            if(mf.mPos > (kMtMaxValForNormalize - kMtHashBlockSize))
                            {
                                uint subValue = (mf.mPos - mf.mHistorySize - 1);
                                mf.MatchFinder_ReduceOffsets(subValue);
                                CMatchFinder.MatchFinder_Normalize3(subValue, P.From(mf.mHash, mf.mFixedHashSize), mf.mHashMask + 1);
                            }
                            {
                                P<uint> heads = P.From(mt.mHashBuf, ((numProcessedBlocks++) & kMtHashNumBlocksMask) * kMtHashBlockSize);
                                uint num = mf.mStreamPos - mf.mPos;
                                heads[0] = 2;
                                heads[1] = num;
                                if(num >= mf.mNumHashBytes)
                                {
                                    num = num - mf.mNumHashBytes + 1;
                                    if(num > kMtHashBlockSize - 2)
                                        num = kMtHashBlockSize - 2;
                                    mt.mInterface.GetHeadsFunc(mf.mBuffer, mf.mPos, P.From(mf.mHash, mf.mFixedHashSize), mf.mHashMask, heads + 2, num);
                                    heads[0] += num;
                                }
                                mf.mPos += num;
                                mf.mBuffer += num;
                            }
                        }

                        Semaphore_Release1(p.mFilledSemaphore);
                    }
                }
            }

            internal void BtGetMatches(P<uint> distances)
            {
                uint numProcessed = 0;
                uint curPos = 2;
                uint limit = kMtBtBlockSize - (mMatchMaxLen * 2);

                distances[1] = mHashNumAvail;

                while(curPos < limit)
                {
                    if(mHashBufPos == mHashBufPosLimit)
                    {
                        MatchFinderMt_GetNextBlock_Hash();
                        distances[1] = numProcessed + mHashNumAvail;

                        if(mHashNumAvail >= mNumHashBytes)
                            continue;

                        while(mHashNumAvail != 0)
                        {
                            distances[curPos++] = 0;
                            mHashNumAvail--;
                        }

                        break;
                    }
                    {
                        TR("BtGetMatches:cyclicBufferPos0", mCyclicBufferPos);

                        uint size = mHashBufPosLimit - mHashBufPos;
                        uint lenLimit = mMatchMaxLen;
                        uint pos = mPos;
                        uint cyclicBufferPos = mCyclicBufferPos;
                        
                        if(lenLimit >= mHashNumAvail)
                            lenLimit = mHashNumAvail;

                        {
                            uint size2 = mHashNumAvail - lenLimit + 1;
                            if(size2 < size)
                                size = size2;

                            size2 = mCyclicBufferSize - cyclicBufferPos;
                            if(size2 < size)
                                size = size2;
                        }

                        while(curPos < limit && size-- != 0)
                        {
                            P<uint> startDistances = distances + curPos;
                            uint num = (uint)(CMatchFinder.GetMatchesSpec1(lenLimit, pos - mHashBuf[mHashBufPos++],
                                pos, mBuffer, mSon, cyclicBufferPos, mCyclicBufferSize, mCutValue,
                                startDistances + 1, mNumHashBytes - 1) - startDistances);
                            TR("GetMatchesSpec1", num);
                            startDistances[0] = num - 1;
                            curPos += num;
                            cyclicBufferPos++;
                            pos++;
                            mBuffer++;
                        }

                        numProcessed += pos - mPos;
                        mHashNumAvail -= pos - mPos;
                        mPos = pos;

                        if(cyclicBufferPos == mCyclicBufferSize)
                            cyclicBufferPos = 0;

                        mCyclicBufferPos = cyclicBufferPos;

                        TR("BtGetMatches:cyclicBufferPos1", mCyclicBufferPos);
                    }
                }

                distances[0] = curPos;
            }

            internal void BtFillBlock(uint globalBlockIndex)
            {
                CMtSync sync = mHashSync;
                if(!sync.mNeedStart)
                {
                    CriticalSection_Enter(sync.mCS);
                    sync.mCsWasEntered = true;
                }

                BtGetMatches(mBtBuf + (globalBlockIndex & kMtBtNumBlocksMask) * kMtBtBlockSize);

                if(mPos > kMtMaxValForNormalize - kMtBtBlockSize)
                {
                    uint subValue = mPos - mCyclicBufferSize;
                    CMatchFinder.MatchFinder_Normalize3(subValue, mSon, mCyclicBufferSize * 2);
                    mPos -= subValue;
                }

                if(!sync.mNeedStart)
                {
                    CriticalSection_Leave(sync.mCS);
                    sync.mCsWasEntered = false;
                }
            }

            internal void BtThreadFunc()
            {
                CMatchFinderMt mt = this;
                CMtSync p = mt.mBtSync;
                for(; ; )
                {
                    Event_Wait(p.mCanStart);
                    Event_Set(p.mWasStarted);

                    uint blockIndex = 0;
                    for(; ; )
                    {
                        if(p.mExit)
                            return;
                        
                        if(SyncTrace.Enable)
                            Trace.MatchObjectWait(p, "BtThreadFunc:stop");

                        if(p.mStopWriting)
                        {
                            if(SyncTrace.Enable)
                                Trace.MatchObjectWait(p, "BtThreadFunc:stop");
                            p.mNumProcessedBlocks = blockIndex;
                            mt.mHashSync.MtSync_StopWriting();
                            Event_Set(p.mWasStopped);
                            break;
                        }

                        if(SyncTrace.Enable)
                            Trace.MatchObjectWait(p, "BtThreadFunc:stop");

                        Semaphore_Wait(p.mFreeSemaphore);
                        mt.BtFillBlock(blockIndex++);
                        Semaphore_Release1(p.mFilledSemaphore);
                    }
                }
            }

            internal SRes MatchFinderMt_Create(uint historySize, uint keepAddBufferBefore, uint matchMaxLen, uint keepAddBufferAfter, ISzAlloc alloc)
            {
                CMatchFinderMt p = this;
                CMatchFinder mf = p.@base;

                p.mHistorySize = historySize;

                if(kMtBtBlockSize <= matchMaxLen * 4)
                    return SZ_ERROR_PARAM;

                if(p.mHashBuf == null)
                {
                    p.mHashBuf = alloc.Alloc<uint>(alloc, kHashBufferSize + kBtBufferSize);
                    if(p.mHashBuf == null)
                        return SZ_ERROR_MEM;

                    p.mBtBuf = P.From(p.mHashBuf, kHashBufferSize);
                }

                keepAddBufferBefore += (kHashBufferSize + kBtBufferSize);
                keepAddBufferAfter += kMtHashBlockSize;

                if(!mf.MatchFinder_Create(historySize, keepAddBufferBefore, matchMaxLen, keepAddBufferAfter, alloc))
                    return SZ_ERROR_MEM;

                SRes res;
                if((res = p.mHashSync.MtSync_Create(p.HashThreadFunc, kMtHashNumBlocks)) != SZ_OK)
                    return res;
                if((res = p.mBtSync.MtSync_Create(p.BtThreadFunc, kMtBtNumBlocks)) != SZ_OK)
                    return res;
                return SZ_OK;
            }

            internal void MatchFinderMt_CreateVTable(out IMatchFinder vTable)
            {
                // Careful: don't use this.mNumHashBytes - it hasn't been initialized yet!
                TR("MatchFinderMt_CreateVTable", @base.mNumHashBytes);
                switch(@base.mNumHashBytes)
                {
                case 2:
                    vTable = mInterface = new MatchFinderMt2();
                    break;
                case 3:
                    vTable = mInterface = new MatchFinderMt3();
                    break;
                default:
#if PROTOTYPE
                    vTable = mInterface = new MatchFinderMt5();
                    break;
                case 4:
#endif
                    if(@base.mBigHash)
                        vTable = mInterface = new MatchFinderMt4b();
                    else
                        vTable = mInterface = new MatchFinderMt4a();
                    break;
                }
            }

            #endregion
        }

        private abstract class MatchFinderMtBase: IMatchFinderMt
        {
            public virtual void Init(object p)
            {
                MatchFinderMt_Init((CMatchFinderMt)p);
            }

            public virtual byte GetIndexByte(object p, int index)
            {
                return ((CMatchFinderMt)p).MatchFinderMt_GetIndexByte(index);
            }

            public virtual uint GetNumAvailableBytes(object p)
            {
                return ((CMatchFinderMt)p).MatchFinderMt_GetNumAvailableBytes();
            }

            public virtual P<byte> GetPointerToCurrentPos(object p)
            {
                return ((CMatchFinderMt)p).MatchFinderMt_GetPointerToCurrentPos();
            }

            public virtual uint GetMatches(object p, P<uint> distances)
            {
                return MatchFinderMt_GetMatches((CMatchFinderMt)p, distances);
            }

            public abstract void Skip(object p, uint num);
            public abstract P<uint> MixMatchesFunc(object p, uint matchMinPos, P<uint> distances);
            public abstract void GetHeadsFunc(P<byte> buffer, uint pos, P<uint> hash, uint hashMask, P<uint> heads, uint numHeads);

            /* Call it after ReleaseStream / SetStream */
            private static void MatchFinderMt_Init(CMatchFinderMt p)
            {
                CMatchFinder mf = p.@base;

                p.mBtBufPos = p.mBtBufPosLimit = 0;
                p.mHashBufPos = p.mHashBufPosLimit = 0;
                mf.MatchFinder_Init();
                p.mPointerToCurPos = mf.MatchFinder_GetPointerToCurrentPos();
                p.mBtNumAvailBytes = 0;
                p.mLzPos = p.mHistorySize + 1;

                p.mHash = mf.mHash;
                p.mFixedHashSize = mf.mFixedHashSize;

                p.mSon = mf.mSon;
                p.mMatchMaxLen = mf.mMatchMaxLen;
                p.mNumHashBytes = mf.mNumHashBytes;
                p.mPos = mf.mPos;
                p.mBuffer = mf.mBuffer;
                p.mCyclicBufferPos = mf.mCyclicBufferPos;
                p.mCyclicBufferSize = mf.mCyclicBufferSize;
                p.mCutValue = mf.mCutValue;
            }

            private static uint MatchFinderMt_GetMatches(CMatchFinderMt p, P<uint> distances)
            {
                TR("MatchFinderMt_GetMatches", p.mBtBufPos);
                P<uint> btBuf = p.mBtBuf + p.mBtBufPos;
                uint len = btBuf[0];
                btBuf++;
                p.mBtBufPos += 1 + len;

                if(len == 0)
                {
                    if(p.mBtNumAvailBytes-- >= 4)
                        len = (uint)(p.mInterface.MixMatchesFunc(p, p.mLzPos - p.mHistorySize, distances) - distances);
                }
                else
                {
                    // Condition: there are matches in btBuf with length < p.numHashBytes
                    p.mBtNumAvailBytes--;
                    P<uint> distances2 = p.mInterface.MixMatchesFunc(p, p.mLzPos - btBuf[1], distances);

                    do
                    {
                        distances2[0] = btBuf[0];
                        distances2++;
                        btBuf++;

                        distances2[0] = btBuf[0];
                        distances2++;
                        btBuf++;

                        len -= 2;
                    }
                    while(len != 0);

                    len = (uint)(distances2 - distances);
                }

                p.mLzPos++;
                p.mPointerToCurPos++;
                return len;
            }
        }

        private sealed class MatchFinderMt2: MatchFinderMtBase
        {
            public override void Skip(object p, uint num)
            {
                MatchFinderMt0_Skip((CMatchFinderMt)p, num);
            }

            public override uint GetMatches(object p, P<uint> distances)
            {
                return MatchFinderMt2_GetMatches((CMatchFinderMt)p, distances);
            }

            public override void GetHeadsFunc(P<byte> buffer, uint pos, P<uint> hash, uint hashMask, P<uint> heads, uint numHeads)
            {
                while(numHeads != 0)
                {
                    uint value = buffer[0] | ((uint)buffer[1] << 8);
                    TR("GetHeads2", value);
                    buffer++;
                    heads[0] = pos - hash[value];
                    heads++;
                    hash[value] = pos++;
                    numHeads--;
                }
            }

            public override P<uint> MixMatchesFunc(object p, uint matchMinPos, P<uint> distances)
            {
                throw new InvalidOperationException(); // was a NULL delegate
            }

            private static void MatchFinderMt0_Skip(CMatchFinderMt p, uint num)
            {
                do
                {
                    if(p.mBtBufPos == p.mBtBufPosLimit)
                        p.MatchFinderMt_GetNextBlock_Bt();

                    p.mBtNumAvailBytes--;
                    p.mLzPos++;
                    p.mPointerToCurPos++;
                    p.mBtBufPos += p.mBtBuf[p.mBtBufPos] + 1;
                }
                while(--num != 0);
            }

            private static uint MatchFinderMt2_GetMatches(CMatchFinderMt p, P<uint> distances)
            {
                P<uint> btBuf = p.mBtBuf + p.mBtBufPos;
                uint len = btBuf[0];
                btBuf++;
                p.mBtBufPos += 1 + len;
                p.mBtNumAvailBytes--;

                for(uint i = 0; i < len; i += 2)
                {
                    distances[0] = btBuf[0];
                    distances++;
                    btBuf++;

                    distances[0] = btBuf[0];
                    distances++;
                    btBuf++;
                }

                p.mLzPos++;
                p.mPointerToCurPos++;
                return len;
            }
        }

        private sealed class MatchFinderMt3: MatchFinderMtBase
        {
            public override void Skip(object p, uint num)
            {
                MatchFinderMt2_Skip((CMatchFinderMt)p, num);
            }

            public override void GetHeadsFunc(P<byte> buffer, uint pos, P<uint> hash, uint hashMask, P<uint> heads, uint numHeads)
            {
                while(numHeads != 0)
                {
                    uint value = (buffer[0].CRC() ^ buffer[1] ^ ((uint)buffer[2] << 8)) & hashMask;
                    TR("GetHeads3", value);
                    buffer++;
                    heads[0] = pos - hash[value];
                    heads++;
                    hash[value] = pos++;
                    numHeads--;
                }
            }

            public override P<uint> MixMatchesFunc(object p, uint matchMinPos, P<uint> distances)
            {
                return MixMatches2((CMatchFinderMt)p, matchMinPos, distances);
            }

            private static P<uint> MixMatches2(CMatchFinderMt p, uint matchMinPos, P<uint> distances)
            {
                P<uint> hash = p.mHash;
                P<byte> cur = p.mPointerToCurPos;
                uint lzPos = p.mLzPos;
                uint hash2Value = (cur[0].CRC() ^ cur[1]) & (kHash2Size - 1);

                uint curMatch2 = hash[hash2Value];
                hash[hash2Value] = lzPos;

                if(curMatch2 >= matchMinPos && cur[(long)curMatch2 - lzPos] == cur[0])
                {
                    distances[0] = 2;
                    distances++;
                    distances[0] = lzPos - curMatch2 - 1;
                    distances++;
                }

                return distances;
            }

            private static void MatchFinderMt2_Skip(CMatchFinderMt p, uint num)
            {
                do
                {
                    if(p.mBtBufPos == p.mBtBufPosLimit)
                        p.MatchFinderMt_GetNextBlock_Bt();

                    if(p.mBtNumAvailBytes-- >= 2)
                    {
                        P<byte> cur = p.mPointerToCurPos;
                        P<uint> hash = p.mHash;
                        uint hash2Value = (cur[0].CRC() ^ cur[1]) & (kHash2Size - 1);
                        hash[hash2Value] = p.mLzPos;
                    }

                    p.mLzPos++;
                    p.mPointerToCurPos++;
                    p.mBtBufPos += p.mBtBuf[p.mBtBufPos] + 1;
                }
                while(--num != 0);
            }
        }

        private abstract class MatchFinderMt4: MatchFinderMtBase
        {
            public override void Skip(object p, uint num)
            {
                MatchFinderMt3_Skip((CMatchFinderMt)p, num);
            }

            public override P<uint> MixMatchesFunc(object p, uint matchMinPos, P<uint> distances)
            {
                return MixMatches3((CMatchFinderMt)p, matchMinPos, distances);
            }

            private static P<uint> MixMatches3(CMatchFinderMt p, uint matchMinPos, P<uint> distances)
            {
                P<uint> hash = p.mHash;
                P<byte> cur = p.mPointerToCurPos;
                uint lzPos = p.mLzPos;
                uint temp = cur[0].CRC() ^ cur[1];
                uint hash2Value = temp & (kHash2Size - 1);
                uint hash3Value = (temp ^ ((uint)cur[2] << 8)) & (kHash3Size - 1);

                uint curMatch2 = hash[hash2Value];
                uint curMatch3 = hash[kFix3HashSize + hash3Value];

                hash[hash2Value] = lzPos;
                hash[kFix3HashSize + hash3Value] = lzPos;

                if(curMatch2 >= matchMinPos && cur[curMatch2 - lzPos] == cur[0])
                {
                    distances[1] = lzPos - curMatch2 - 1;

                    if(cur[curMatch2 - lzPos + 2] == cur[2])
                    {
                        distances[0] = 3;
                        return distances + 2;
                    }

                    distances[0] = 2;
                    distances += 2;
                }

                if(curMatch3 >= matchMinPos && cur[curMatch3 - lzPos] == cur[0])
                {
                    distances[0] = 3;
                    distances++;

                    distances[0] = lzPos - curMatch3 - 1;
                    distances++;
                }

                return distances;
            }

            private static void MatchFinderMt3_Skip(CMatchFinderMt p, uint num)
            {
                do
                {
                    if(p.mBtBufPos == p.mBtBufPosLimit)
                        p.MatchFinderMt_GetNextBlock_Bt();

                    if(p.mBtNumAvailBytes-- >= 3)
                    {
                        P<byte> cur = p.mPointerToCurPos;
                        P<uint> hash = p.mHash;
                        uint temp = cur[0].CRC() ^ cur[1];
                        uint hash2Value = temp & (kHash2Size - 1);
                        uint hash3Value = (temp ^ ((uint)cur[2] << 8)) & (kHash3Size - 1);
                        hash[kFix3HashSize + hash3Value] = p.mLzPos;
                        hash[hash2Value] = p.mLzPos;
                    }

                    p.mLzPos++;
                    p.mPointerToCurPos++;
                    p.mBtBufPos += p.mBtBuf[p.mBtBufPos] + 1;
                }
                while(--num != 0);
            }
        }

        private sealed class MatchFinderMt4a: MatchFinderMt4
        {
            public override void GetHeadsFunc(P<byte> buffer, uint pos, P<uint> hash, uint hashMask, P<uint> heads, uint numHeads)
            {
                while(numHeads != 0)
                {
                    uint value = (buffer[0].CRC() ^ buffer[1] ^ ((uint)buffer[2] << 8) ^ (buffer[3].CRC() << 5)) & hashMask;
                    TR("GetHeads4", value);
                    buffer++;
                    heads[0] = pos - hash[value];
                    heads++;
                    hash[value] = pos++;
                    numHeads--;
                }
            }
        }

        private sealed class MatchFinderMt4b: MatchFinderMt4
        {
            public override void GetHeadsFunc(P<byte> buffer, uint pos, P<uint> hash, uint hashMask, P<uint> heads, uint numHeads)
            {
                while(numHeads != 0)
                {
                    uint value = (buffer[0].CRC() ^ buffer[1] ^ ((uint)buffer[2] << 8) ^ ((uint)buffer[3] << 16)) & hashMask;
                    TR("GetHeads4b", value);
                    buffer++;
                    heads[0] = pos - hash[value];
                    heads++;
                    hash[value] = pos++;
                    numHeads--;
                }
            }
        }

#if PROTOTYPE
        private sealed class MatchFinderMt5: MatchFinderMtBase
        {
            public override void Skip(object p, uint num)
            {
                MatchFinderMt4_Skip((CMatchFinderMt)p, num);
            }

            public override void GetHeadsFunc(P<byte> buffer, uint pos, P<uint> hash, uint hashMask, P<uint> heads, uint numHeads)
            {
                for(; numHeads != 0; numHeads--)
                {
                    uint value = ((buffer[0].CRC() ^ buffer[1] ^ ((uint)buffer[2] << 8) ^ (buffer[3].CRC() << 5) ^ (buffer[4].CRC() << 3)) & hashMask);
                    buffer++;
                    heads[0] = pos - hash[value];
                    heads++;
                    hash[value] = pos++;
                }
            }

            public override P<uint> MixMatchesFunc(object p, uint matchMinPos, P<uint> distances)
            {
                return MixMatches4((CMatchFinderMt)p, matchMinPos, distances);
            }

            private static void MatchFinderMt4_Skip(CMatchFinderMt p, uint count)
            {
                do
                {
                    if(p.mBtBufPos == p.mBtBufPosLimit)
                        p.MatchFinderMt_GetNextBlock_Bt();

                    if(p.mBtNumAvailBytes-- >= 4)
                    {
                        P<byte> cur = p.mPointerToCurPos;
                        P<uint> hash = p.mHash;
                        uint temp = cur[0].CRC() ^ cur[1];
                        uint hash2Value = temp & (kHash2Size - 1);
                        uint hash3Value = (temp ^ ((uint)cur[2] << 8)) & (kHash3Size - 1);
                        uint hash4Value = (temp ^ ((uint)cur[2] << 8) ^ (cur[3].CRC() << 5)) & (kHash4Size - 1);
                        hash[kFix4HashSize + hash4Value] = p.mLzPos;
                        hash[kFix3HashSize + hash3Value] = p.mLzPos;
                        hash[hash2Value] = p.mLzPos;
                    }

                    p.mLzPos++;
                    p.mPointerToCurPos++;
                    p.mBtBufPos += p.mBtBuf[p.mBtBufPos] + 1;
                }
                while(--count != 0);
            }

            private static P<uint> MixMatches4(CMatchFinderMt p, uint matchMinPos, P<uint> distances)
            {
                P<uint> hash = p.mHash;
                P<byte> cur = p.mPointerToCurPos;
                uint lzPos = p.mLzPos;

                uint temp = cur[0].CRC() ^ cur[1];
                uint hash2Value = temp & (kHash2Size - 1);
                uint hash3Value = (temp ^ ((uint)cur[2] << 8)) & (kHash3Size - 1);
                uint hash4Value = (temp ^ ((uint)cur[2] << 8) ^ (cur[3].CRC() << 5)) & (kHash4Size - 1);

                uint curMatch2 = hash[hash2Value];
                uint curMatch3 = hash[kFix3HashSize + hash3Value];
                uint curMatch4 = hash[kFix4HashSize + hash4Value];

                hash[hash2Value] = lzPos;
                hash[kFix3HashSize + hash3Value] = lzPos;
                hash[kFix4HashSize + hash4Value] = lzPos;

                if(curMatch2 >= matchMinPos && cur[(long)curMatch2 - lzPos] == cur[0])
                {
                    distances[1] = lzPos - curMatch2 - 1;
                    if(cur[(long)curMatch2 - lzPos + 2] == cur[2])
                    {
                        distances[0] = (cur[(long)curMatch2 - lzPos + 3] == cur[3]) ? 4u : 3u;
                        return distances + 2;
                    }

                    distances[0] = 2;
                    distances += 2;
                }

                if(curMatch3 >= matchMinPos && cur[(long)curMatch3 - lzPos] == cur[0])
                {
                    distances[1] = lzPos - curMatch3 - 1;
                    if(cur[(long)curMatch3 - lzPos + 3] == cur[3])
                    {
                        distances[0] = 4;
                        return distances + 2;
                    }

                    distances[0] = 3;
                    distances += 2;
                }

                if(curMatch4 >= matchMinPos
                    && cur[(long)curMatch4 - lzPos] == cur[0]
                    && cur[(long)curMatch4 - lzPos + 3] == cur[3])
                {
                    distances[0] = 4;
                    distances++;

                    distances[0] = lzPos - curMatch4 - 1;
                    distances++;
                }

                return distances;
            }
        }
#endif
    }
}
