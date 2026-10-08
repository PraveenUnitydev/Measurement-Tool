// ─────────────────────────────────────────────────────────────────────────────
// DAS clip section - shared clipping code.
//
// The clip plane is set from C# with global floats (ClipSectionMode, ClipSectionController, VisualComparisonMode,
// ClipComparisonController):
//   _GlobalClip{X,Y,Z}Enabled   1 = this axis cuts
//   _GlobalClip{X,Y,Z}Position  world position of the plane on that axis
//   _GlobalClip{X,Y,Z}Direction +1 removes the side above the plane, -1 the side below
//
// Used by:
//   * ClipSection_Lit.shader (the stand-in material used when a vehicle's own shader can't clip), and
//   * the vehicle Shader Graphs, through a Custom Function node (File mode, function "DASClipThreshold")
//     wired into Alpha Clip Threshold. A graph that does this keeps its own look (car paint, clear coat,
//     reflections, glass) while clipping, and the app no longer swaps its materials.
//
// Keep this file's GUID (DASClip.hlsl.meta): the Shader Graph Custom Function nodes point at it.
// ─────────────────────────────────────────────────────────────────────────────
#ifndef DAS_CLIP_INCLUDED
#define DAS_CLIP_INCLUDED

float _GlobalClipXEnabled;
float _GlobalClipXPosition;
float _GlobalClipXDirection;
float _GlobalClipYEnabled;
float _GlobalClipYPosition;
float _GlobalClipYDirection;
float _GlobalClipZEnabled;
float _GlobalClipZPosition;
float _GlobalClipZDirection;

// True when this world position is on the removed side of the active clip plane.
bool DASIsClipped(float3 positionWS)
{
    if (_GlobalClipXEnabled > 0.5 && (positionWS.x - _GlobalClipXPosition) * _GlobalClipXDirection > 0.0) return true;
    if (_GlobalClipYEnabled > 0.5 && (positionWS.y - _GlobalClipYPosition) * _GlobalClipYDirection > 0.0) return true;
    if (_GlobalClipZEnabled > 0.5 && (positionWS.z - _GlobalClipZPosition) * _GlobalClipZDirection > 0.0) return true;
    return false;
}

// Discard the pixel if it is cut away.
void DASClip(float3 positionWS)
{
    if (DASIsClipped(positionWS)) clip(-1.0);
}

// Shader Graph Custom Function (File mode), name "DASClipThreshold".
// Wire WorldPos <- Position node (World space) and Threshold -> Alpha Clip Threshold (with Alpha Clipping on).
// Kept pixels get threshold 0 (any alpha >= 0 passes); removed pixels get 2 (no alpha reaches it).
void DASClipThreshold_float(float3 WorldPos, out float Threshold)
{
#if defined(SHADERGRAPH_PREVIEW)
    Threshold = 0.0;
#else
    Threshold = DASIsClipped(WorldPos) ? 2.0 : 0.0;
#endif
}

void DASClipThreshold_half(half3 WorldPos, out half Threshold)
{
#if defined(SHADERGRAPH_PREVIEW)
    Threshold = 0.0;
#else
    Threshold = DASIsClipped((float3)WorldPos) ? 2.0 : 0.0;
#endif
}

#endif // DAS_CLIP_INCLUDED
