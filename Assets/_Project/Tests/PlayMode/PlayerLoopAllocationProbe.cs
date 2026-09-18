#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine.Profiling;

/// <summary>
/// Measures the managed memory allocated by game code during a window of frames. The editor's
/// own per-frame allocations (test runner, asset pipeline, GUI) make whole-frame counters useless
/// for a zero-allocation budget, so the probe records the profiler and sums only the GC.Alloc
/// samples nested under the player loop, skipping the test framework's subtree.
/// </summary>
public sealed class PlayerLoopAllocationProbe
{
    private const string PlayerLoopMarker = "PlayerLoop";
    private const string AllocationMarker = "GC.Alloc";
    private static readonly string[] ExcludedMarkerFragments = { "Test", "Profiler" };

    private readonly List<FrameDataView.MarkerInfo> markers = new List<FrameDataView.MarkerInfo>(1024);
    private readonly HashSet<int> excludedMarkers = new HashSet<int>();
    private int windowStartFrame;

    public long Bytes { get; private set; }
    public int Frames { get; private set; }
    public long AverageBytesPerFrame => Frames > 0 ? Bytes / Frames : 0;

    public static void StartRecording()
    {
        ProfilerDriver.profileEditor = false;
        ProfilerDriver.deepProfiling = false;
        ProfilerDriver.enabled = true;
        Profiler.enabled = true;
    }

    public static void StopRecording()
    {
        Profiler.enabled = false;
        ProfilerDriver.enabled = false;
    }

    /// <summary>Marks the first frame of a window; call before yielding the frames to measure.</summary>
    public void BeginWindow()
    {
        windowStartFrame = ProfilerDriver.lastFrameIndex + 1;
    }

    /// <summary>Accumulates every frame recorded since <see cref="BeginWindow"/>.</summary>
    public void EndWindow()
    {
        int lastFrame = ProfilerDriver.lastFrameIndex;
        for (int frame = windowStartFrame; frame <= lastFrame; frame++)
        {
            using RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(frame, 0);
            if (view == null || !view.valid) continue;

            Bytes += SumPlayerLoopAllocations(view);
            Frames++;
        }
    }

    private long SumPlayerLoopAllocations(RawFrameDataView view)
    {
        int allocationMarker = view.GetMarkerId(AllocationMarker);
        int playerLoopMarker = view.GetMarkerId(PlayerLoopMarker);
        if (allocationMarker == FrameDataView.invalidMarkerId || playerLoopMarker == FrameDataView.invalidMarkerId) return 0;

        CollectExcludedMarkers(view);

        long total = 0;
        int includedUntil = -1;
        int excludedUntil = -1;

        for (int i = 0; i < view.sampleCount; i++)
        {
            int marker = view.GetSampleMarkerId(i);
            int subtreeEnd = i + view.GetSampleChildrenCountRecursive(i);

            if (i > excludedUntil) excludedUntil = -1;
            if (excludedUntil < 0 && excludedMarkers.Contains(marker))
            {
                excludedUntil = subtreeEnd;
                continue;
            }

            if (marker == playerLoopMarker) includedUntil = subtreeEnd;
            if (i > includedUntil || excludedUntil >= 0 || marker != allocationMarker) continue;

            total += view.GetSampleMetadataAsLong(i, 0);
        }

        return total;
    }

    private void CollectExcludedMarkers(RawFrameDataView view)
    {
        excludedMarkers.Clear();
        markers.Clear();
        view.GetMarkers(markers);

        for (int i = 0; i < markers.Count; i++)
        {
            string name = markers[i].name;
            for (int j = 0; j < ExcludedMarkerFragments.Length; j++)
            {
                if (name.IndexOf(ExcludedMarkerFragments[j], System.StringComparison.Ordinal) < 0) continue;

                excludedMarkers.Add(markers[i].id);
                break;
            }
        }
    }
}
#endif
