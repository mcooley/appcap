using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Media.Audio;
using global::Windows.Win32.System.Com;
using global::Windows.Win32.System.Com.StructuredStorage;
using global::Windows.Win32.System.Variant;

namespace AppCap.Windows;

internal sealed record RecordingAudioPacket(
    byte[] Data,
    uint FrameCount,
    TimeSpan Timestamp,
    bool Discontinuous,
    bool TimestampError);

internal interface IRecordingAudioCapture : IDisposable
{
    Task Completion { get; }

    Task StartAsync(Action<RecordingAudioPacket> packetHandler, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

internal sealed class ProcessLoopbackAudioCapture(int processId) : IRecordingAudioCapture
{
    internal const uint SamplesPerSecond = 44_100;
    internal const ushort ChannelCount = 2;
    internal const ushort BitsPerSample = 16;
    internal const ushort BytesPerFrame = ChannelCount * BitsPerSample / 8;

    private const ushort WaveFormatPcm = 1;
    private const string ProcessLoopbackDevice = "VAD\\Process_Loopback";

    private readonly AutoResetEvent sampleReady = new(initialState: false);
    private readonly CancellationTokenSource stopCancellation = new();
    private ComPtr<IAudioClient>? audioClient;
    private ComPtr<IAudioCaptureClient>? captureClient;
    private CancellationTokenSource? captureCancellation;
    private Action<RecordingAudioPacket>? packetHandler;
    private Task captureTask = Task.CompletedTask;
    private bool started;
    private bool disposed;

    public Task Completion => captureTask;

    public async Task StartAsync(Action<RecordingAudioPacket> packetHandler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packetHandler);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            throw new InvalidOperationException("Audio capture has already started.");
        }

        started = true;
        this.packetHandler = packetHandler;
        try
        {
            audioClient = await ActivateAudioClientAsync(cancellationToken).ConfigureAwait(false);
            InitializeAudioClient();
            captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopCancellation.Token);
            StartAudioClient();
            captureTask = Task.Run(() => CaptureLoop(captureCancellation.Token), CancellationToken.None);
        }
        catch
        {
            ReleaseAudioClient();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!started)
        {
            return;
        }

        stopCancellation.Cancel();
        sampleReady.Set();
        await captureTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stopCancellation.Cancel();
        sampleReady.Set();
        try
        {
            captureTask.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }

        ReleaseAudioClient();
        captureCancellation?.Dispose();
        stopCancellation.Dispose();
        sampleReady.Dispose();
    }

    private async Task<ComPtr<IAudioClient>> ActivateAudioClientAsync(CancellationToken cancellationToken)
    {
        using AudioActivationRequest request = BeginAudioActivation();
        nint activatedInterface;
        try
        {
            activatedInterface = await request.Completion.ConfigureAwait(false);
        }
        catch (COMException exception)
        {
            throw AudioFailure("activation", exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return CreateAudioClient(activatedInterface);
    }

    private unsafe AudioActivationRequest BeginAudioActivation()
    {
        AUDIOCLIENT_ACTIVATION_PARAMS activationParameters = new()
        {
            ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
        };
        activationParameters.ProcessLoopbackParams.TargetProcessId = checked((uint)processId);
        activationParameters.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE.PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;

        PROPVARIANT activationVariant = new();
        activationVariant.vt = VARENUM.VT_BLOB;
        activationVariant.blob.cbSize = (uint)sizeof(AUDIOCLIENT_ACTIVATION_PARAMS);
        activationVariant.blob.pBlobData = (byte*)&activationParameters;

        AudioActivationCompletionHandler completionHandler = new();
        IActivateAudioInterfaceAsyncOperation* operation = null;
        Guid audioClientId = typeof(IAudioClient).GUID;
        try
        {
            HRESULT result = PInvoke.ActivateAudioInterfaceAsync(
                ProcessLoopbackDevice,
                audioClientId,
                activationVariant,
                completionHandler.Pointer,
                &operation);
            ThrowOnAudioFailure(result, "activation request");
            return new AudioActivationRequest(completionHandler, new ComPtr<IActivateAudioInterfaceAsyncOperation>(operation));
        }
        catch
        {
            completionHandler.Dispose();
            throw;
        }
    }

    private static unsafe ComPtr<IAudioClient> CreateAudioClient(nint pointer) => new((IAudioClient*)pointer);

    private unsafe void InitializeAudioClient()
    {
        IAudioClient* client = audioClient?.Get();
        if (client is null)
        {
            throw new InvalidOperationException("Audio client activation did not complete.");
        }

        WAVEFORMATEX format = new()
        {
            wFormatTag = WaveFormatPcm,
            nChannels = ChannelCount,
            nSamplesPerSec = SamplesPerSecond,
            nAvgBytesPerSec = SamplesPerSecond * BytesPerFrame,
            nBlockAlign = BytesPerFrame,
            wBitsPerSample = BitsPerSample,
        };
        uint streamFlags = PInvoke.AUDCLNT_STREAMFLAGS_LOOPBACK |
            PInvoke.AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
            PInvoke.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM |
            PInvoke.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
        HRESULT initializeResult = client->Initialize(
            AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
            streamFlags,
            0,
            0,
            format,
            null);
        ThrowOnAudioFailure(initializeResult, "stream initialization");
        HRESULT serviceResult = client->GetService(out IAudioCaptureClient* captureClientPointer);
        ThrowOnAudioFailure(serviceResult, "capture service creation");
        captureClient = new ComPtr<IAudioCaptureClient>(captureClientPointer);
        HRESULT eventResult = client->SetEventHandle(new HANDLE(sampleReady.SafeWaitHandle.DangerousGetHandle()));
        ThrowOnAudioFailure(eventResult, "sample event registration");
    }

    private unsafe void StartAudioClient()
    {
        IAudioClient* client = audioClient?.Get();
        if (client is null)
        {
            throw new InvalidOperationException("Audio client activation did not complete.");
        }

        ThrowOnAudioFailure(client->Start(), "stream start");
    }

    private unsafe void CaptureLoop(CancellationToken cancellationToken)
    {
        WaitHandle[] waitHandles = [sampleReady, cancellationToken.WaitHandle];
        try
        {
            while (WaitHandle.WaitAny(waitHandles) == 0)
            {
                DrainPackets();
            }

            DrainPackets();
        }
        finally
        {
            if (audioClient is not null)
            {
                audioClient.Get()->Stop().ThrowOnFailure();
            }
        }
    }

    private unsafe void DrainPackets()
    {
        IAudioCaptureClient* client = captureClient?.Get();
        if (client is null)
        {
            throw new InvalidOperationException("Audio capture is not initialized.");
        }
        while (true)
        {
            client->GetNextPacketSize(out uint availableFrames).ThrowOnFailure();
            if (availableFrames == 0)
            {
                return;
            }

            byte* source = null;
            uint frameCount = 0;
            uint rawFlags = 0;
            ulong devicePosition = 0;
            ulong qpcPosition = 0;
            client->GetBuffer(&source, &frameCount, &rawFlags, &devicePosition, &qpcPosition).ThrowOnFailure();
            try
            {
                _AUDCLNT_BUFFERFLAGS flags = (_AUDCLNT_BUFFERFLAGS)rawFlags;
                int byteCount = checked((int)(frameCount * BytesPerFrame));
                byte[] data = new byte[byteCount];
                if ((flags & _AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) == 0)
                {
                    new ReadOnlySpan<byte>(source, byteCount).CopyTo(data);
                }

                packetHandler!(new RecordingAudioPacket(
                    data,
                    frameCount,
                    TimeSpan.FromTicks(checked((long)qpcPosition)),
                    (flags & _AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) != 0,
                    (flags & _AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) != 0));
            }
            finally
            {
                client->ReleaseBuffer(frameCount).ThrowOnFailure();
            }
        }
    }

    private void ReleaseAudioClient()
    {
        captureClient?.Dispose();
        captureClient = null;
        audioClient?.Dispose();
        audioClient = null;
    }

    private static void ThrowOnAudioFailure(HRESULT result, string operation)
    {
        if (result.Failed)
        {
            Exception innerException = Marshal.GetExceptionForHR(result.Value) ?? new InvalidOperationException("The audio operation failed.");
            throw new AppCapException($"Process audio capture {operation} failed (0x{result.Value:X8}).", innerException);
        }
    }

    private static AppCapException AudioFailure(string operation, COMException exception) =>
        new($"Process audio capture {operation} failed (0x{exception.HResult:X8}).", exception);
}

internal sealed class AudioActivationRequest(
    AudioActivationCompletionHandler completionHandler,
    ComPtr<IActivateAudioInterfaceAsyncOperation> operation) : IDisposable
{
    public Task<nint> Completion => completionHandler.Completion;

    public void Dispose()
    {
        operation.Dispose();
        completionHandler.Dispose();
    }
}

internal sealed unsafe class AudioActivationCompletionHandler : IDisposable
{
    private static readonly Guid IUnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IAgileObjectId = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
    private static readonly Guid CompletionHandlerId = new("41D949AB-9862-444A-80F6-C261334DA5EB");
    private static readonly Vtable* SharedVtable = CreateVtable();

    private readonly TaskCompletionSource<nint> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NativeObject* nativeObject;

    public AudioActivationCompletionHandler()
    {
        GCHandle context = GCHandle.Alloc(this);
        nativeObject = (NativeObject*)NativeMemory.AllocZeroed((nuint)sizeof(NativeObject));
        nativeObject->Vtable = SharedVtable;
        nativeObject->Context = GCHandle.ToIntPtr(context);
        nativeObject->ReferenceCount = 1;
    }

    public Task<nint> Completion => completion.Task;

    public IActivateAudioInterfaceCompletionHandler* Pointer => (IActivateAudioInterfaceCompletionHandler*)nativeObject;

    public void Dispose()
    {
        NativeObject* instance = nativeObject;
        nativeObject = null;
        if (instance is not null)
        {
            _ = ReleaseCore(instance);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT QueryInterface(NativeObject* instance, Guid* interfaceId, void** result)
    {
        if (result is null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        if (interfaceId is not null && (*interfaceId == IUnknownId || *interfaceId == IAgileObjectId || *interfaceId == CompletionHandlerId))
        {
            *result = instance;
            _ = AddRefCore(instance);
            return HRESULT.S_OK;
        }

        *result = null;
        return (HRESULT)unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(NativeObject* instance) => AddRefCore(instance);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(NativeObject* instance) => ReleaseCore(instance);

    private static uint AddRefCore(NativeObject* instance) => (uint)Interlocked.Increment(ref instance->ReferenceCount);

    private static uint ReleaseCore(NativeObject* instance)
    {
        int references = Interlocked.Decrement(ref instance->ReferenceCount);
        if (references == 0)
        {
            GCHandle.FromIntPtr(instance->Context).Free();
            NativeMemory.Free(instance);
        }

        return (uint)references;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT ActivateCompleted(NativeObject* instance, IActivateAudioInterfaceAsyncOperation* operation)
    {
        try
        {
            AudioActivationCompletionHandler handler = (AudioActivationCompletionHandler)GCHandle.FromIntPtr(instance->Context).Target!;
            IUnknown* activatedInterface = null;
            HRESULT result = operation->GetActivateResult(out HRESULT activationResult, &activatedInterface);
            if (result.Failed)
            {
                handler.completion.TrySetException(Marshal.GetExceptionForHR(result.Value)!);
            }
            else if (activationResult.Failed)
            {
                handler.completion.TrySetException(Marshal.GetExceptionForHR(activationResult.Value)!);
            }
            else
            {
                handler.completion.TrySetResult((nint)activatedInterface);
            }
        }
        catch (Exception exception)
        {
            return (HRESULT)exception.HResult;
        }

        return HRESULT.S_OK;
    }

    private static Vtable* CreateVtable()
    {
        Vtable* vtable = (Vtable*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(AudioActivationCompletionHandler), sizeof(Vtable));
        vtable->QueryInterface = &QueryInterface;
        vtable->AddRef = &AddRef;
        vtable->Release = &Release;
        vtable->ActivateCompleted = &ActivateCompleted;
        return vtable;
    }

    private struct NativeObject
    {
        public Vtable* Vtable;
        public nint Context;
        public int ReferenceCount;
    }

    private struct Vtable
    {
        public delegate* unmanaged[Stdcall]<NativeObject*, Guid*, void**, HRESULT> QueryInterface;
        public delegate* unmanaged[Stdcall]<NativeObject*, uint> AddRef;
        public delegate* unmanaged[Stdcall]<NativeObject*, uint> Release;
        public delegate* unmanaged[Stdcall]<NativeObject*, IActivateAudioInterfaceAsyncOperation*, HRESULT> ActivateCompleted;
    }
}