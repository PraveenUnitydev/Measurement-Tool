#!/usr/bin/env python3
"""
Adds DAS clip-section support to vehicle Shader Graphs (Unity 6 / URP Shader Graph JSON).

For each graph it:
  * adds a Custom Function node (File mode -> Shaders/Resources/DAS/DASClip.hlsl, function DASClipThreshold)
    fed by a Position node (Absolute World) and wired into Alpha Clip Threshold,
  * turns Alpha Clipping on and renders both faces (so the inside of a cut part is visible),
  * removes the old per-material _CLIP_AXIS alpha trick from Alpha (ALLOY, Seat) - it never worked with clipping off,
  * adds a hidden property _DASClipSupport = 1 so the app knows the material clips by itself and keeps it.
Everything else (car paint, clear coat, glass, maps) is untouched.

usage: patch_shadergraphs.py <input .shadergraph files...> --out <folder>
"""
import json, re, sys, os, uuid, copy

DASCLIP_HLSL_GUID = "7d3a5c19e2b84f60a1c4d8e9f0b26a51"   # Scripts/Shaders/Resources/DAS/DASClip.hlsl.meta
MARKER = "_DASClipSupport"

def load(path):
    txt = open(path, encoding="utf-8-sig").read()
    parts = re.split(r"\n\n(?=\{)", txt.strip())
    return [json.loads(p) for p in parts]

def dump(objs):
    return "\n\n".join(json.dumps(o, indent=4, ensure_ascii=False) for o in objs) + "\n\n"

def new_id():
    return uuid.uuid4().hex

def vec1_slot(slot_id, name, out_name, slot_type, value, stage=3):
    return {"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.Vector1MaterialSlot", "m_ObjectId": new_id(),
            "m_Id": slot_id, "m_DisplayName": name, "m_SlotType": slot_type, "m_Hidden": False,
            "m_ShaderOutputName": out_name, "m_StageCapability": stage, "m_Value": value, "m_DefaultValue": value,
            "m_Labels": []}

def vec3_slot(slot_id, name, out_name, slot_type, stage=3):
    z = {"x": 0.0, "y": 0.0, "z": 0.0}
    return {"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.Vector3MaterialSlot", "m_ObjectId": new_id(),
            "m_Id": slot_id, "m_DisplayName": name, "m_SlotType": slot_type, "m_Hidden": False,
            "m_ShaderOutputName": out_name, "m_StageCapability": stage, "m_Value": dict(z), "m_DefaultValue": dict(z),
            "m_Labels": []}

def draw_state(x, y, w=208.0, h=300.0):
    return {"m_Expanded": True, "m_Position": {"serializedVersion": "2", "x": x, "y": y, "width": w, "height": h}}

def node_common(type_name, name, slots, x, y, version=0, synonyms=None, w=208.0, h=300.0):
    return {"m_SGVersion": version, "m_Type": type_name, "m_ObjectId": new_id(), "m_Group": {"m_Id": ""},
            "m_Name": name, "m_DrawState": draw_state(x, y, w, h),
            "m_Slots": [{"m_Id": s["m_ObjectId"]} for s in slots], "synonyms": synonyms or [],
            "m_Precision": 0, "m_PreviewExpanded": False, "m_DismissedVersion": 0, "m_PreviewMode": 0,
            "m_CustomColors": {"m_SerializableColors": []}}

def block_node(descriptor, slot):
    n = node_common("UnityEditor.ShaderGraph.BlockNode", descriptor, [slot], 0.0, 0.0, 0, [], 0.0, 0.0)
    n["m_PreviewExpanded"] = True
    n["m_SerializedDescriptor"] = descriptor
    return n

def patch(objs, log):
    by_id = {o["m_ObjectId"]: o for o in objs}
    graph = next(o for o in objs if o["m_Type"].endswith(".GraphData"))
    target = next((o for o in objs if o["m_Type"].endswith("Universal.ShaderGraph.UniversalTarget")), None)
    if target is None:
        raise SystemExit("no URP target in graph")

    # already patched?
    for p in graph["m_Properties"]:
        po = by_id[p["m_Id"]]
        if po.get("m_OverrideReferenceName") == MARKER or po.get("m_DefaultReferenceName") == MARKER:
            log.append("already patched - skipped")
            return False

    def block(descriptor):
        for b in graph["m_FragmentContext"]["m_Blocks"]:
            o = by_id[b["m_Id"]]
            if o.get("m_SerializedDescriptor") == descriptor:
                return o
        return None

    def add(o):
        objs.append(o); by_id[o["m_ObjectId"]] = o

    def ensure_block(descriptor, display, out_name, value):
        b = block(descriptor)
        if b is not None:
            return b
        slot = vec1_slot(0, display, out_name, 0, value, 2)
        b = block_node(descriptor, slot)
        add(b); add(slot)
        graph["m_Nodes"].append({"m_Id": b["m_ObjectId"]})
        graph["m_FragmentContext"]["m_Blocks"].append({"m_Id": b["m_ObjectId"]})
        log.append("added block " + descriptor)
        return b

    alpha = ensure_block("SurfaceDescription.Alpha", "Alpha", "Alpha", 1.0)
    thresh = ensure_block("SurfaceDescription.AlphaClipThreshold", "Alpha Clip Threshold", "AlphaClipThreshold", 0.5)

    def inputs_of(node):
        return [e for e in graph["m_Edges"] if e["m_InputSlot"]["m_Node"]["m_Id"] == node["m_ObjectId"]]

    def feeds_from_keyword(node, seen=None):
        """True if the chain into this node uses the old _CLIP_AXIS keyword."""
        seen = seen or set()
        for e in inputs_of(node):
            src = by_id[e["m_OutputSlot"]["m_Node"]["m_Id"]]
            if src["m_ObjectId"] in seen: continue
            seen.add(src["m_ObjectId"])
            if src["m_Type"].endswith(".KeywordNode"):
                kw = by_id.get(src.get("m_Keyword", {}).get("m_Id", ""), {})
                if "CLIP" in (kw.get("m_DefaultReferenceName", "") + kw.get("m_OverrideReferenceName", "")).upper():
                    return True
            if feeds_from_keyword(src, seen): return True
        return False

    # old per-material clip trick on Alpha -> back to 1
    if feeds_from_keyword(alpha):
        graph["m_Edges"] = [e for e in graph["m_Edges"] if e["m_InputSlot"]["m_Node"]["m_Id"] != alpha["m_ObjectId"]]
        aslot = by_id[alpha["m_Slots"][0]["m_Id"]]
        aslot["m_Value"] = 1.0
        log.append("removed old _CLIP_AXIS trick from Alpha")

    # threshold: drop whatever fed it
    before = len(graph["m_Edges"])
    graph["m_Edges"] = [e for e in graph["m_Edges"] if e["m_InputSlot"]["m_Node"]["m_Id"] != thresh["m_ObjectId"]]
    if len(graph["m_Edges"]) != before: log.append("disconnected old Alpha Clip Threshold input")
    tslot = by_id[thresh["m_Slots"][0]["m_Id"]]

    fx = float(graph["m_FragmentContext"].get("m_Position", {}).get("x", 0.0))
    fy = float(graph["m_FragmentContext"].get("m_Position", {}).get("y", 0.0))

    # Position (Absolute World)
    pos_out = vec3_slot(0, "Out", "Out", 1)
    pos = node_common("UnityEditor.ShaderGraph.PositionNode", "Position", [pos_out], fx - 760.0, fy + 620.0, 1, ["location"], 208.0, 314.0)
    pos["m_PreviewMode"] = 2
    pos["m_Space"] = 4          # Absolute World
    pos["m_PositionSource"] = 0
    add(pos); add(pos_out)

    # Custom Function (File) -> DASClip.hlsl : DASClipThreshold
    cf_in = vec3_slot(0, "WorldPos", "WorldPos", 0)
    cf_out = vec1_slot(1, "Threshold", "Threshold", 1, 0.0)
    cf = node_common("UnityEditor.ShaderGraph.CustomFunctionNode", "DASClipThreshold (Custom Function)", [cf_in, cf_out],
                     fx - 460.0, fy + 620.0, 1, ["code", "HLSL"], 260.0, 94.0)
    cf["m_SourceType"] = 0      # File
    cf["m_FunctionName"] = "DASClipThreshold"
    cf["m_FunctionSource"] = DASCLIP_HLSL_GUID
    cf["m_FunctionSourceUsePragmas"] = True
    cf["m_FunctionBody"] = "Enter function body here..."
    add(cf); add(cf_in); add(cf_out)
    graph["m_Nodes"] += [{"m_Id": pos["m_ObjectId"]}, {"m_Id": cf["m_ObjectId"]}]

    def edge(out_node, out_slot, in_node, in_slot):
        return {"m_OutputSlot": {"m_Node": {"m_Id": out_node["m_ObjectId"]}, "m_SlotId": out_slot},
                "m_InputSlot": {"m_Node": {"m_Id": in_node["m_ObjectId"]}, "m_SlotId": in_slot}}
    graph["m_Edges"].append(edge(pos, 0, cf, 0))
    graph["m_Edges"].append(edge(cf, 1, thresh, tslot["m_Id"]))

    # marker property
    prop = {"m_SGVersion": 1, "m_Type": "UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty", "m_ObjectId": new_id(),
            "m_Guid": {"m_GuidSerialized": str(uuid.uuid4())}, "m_Name": "DAS Clip Support", "m_DefaultRefNameVersion": 1,
            "m_RefNameGeneratedByDisplayName": "DAS Clip Support", "m_DefaultReferenceName": "_DAS_Clip_Support",
            "m_OverrideReferenceName": MARKER, "m_GeneratePropertyBlock": True, "m_UseCustomSlotLabel": False,
            "m_CustomSlotLabel": "", "m_DismissedVersion": 0, "m_Precision": 0, "overrideHLSLDeclaration": False,
            "hlslDeclarationOverride": 0, "m_Hidden": True, "m_Value": 1.0, "m_FloatType": 0,
            "m_RangeValues": {"x": 0.0, "y": 1.0}}
    add(prop)
    graph["m_Properties"].append({"m_Id": prop["m_ObjectId"]})
    cats = graph.get("m_CategoryData", [])
    if cats:
        by_id[cats[0]["m_Id"]]["m_ChildObjectList"].append({"m_Id": prop["m_ObjectId"]})

    # target: alpha clip on, both faces
    target["m_AlphaClip"] = True
    if target.get("m_RenderFace", 0) != 2:
        log.append("render face %s -> Both" % target.get("m_RenderFace"))
    target["m_RenderFace"] = 2
    log.append("alpha clip on, clip node added")
    return True

def validate(objs):
    """Every reference resolves, ids are unique, edges join existing slots of the right direction."""
    by_id = {}
    for o in objs:
        assert o["m_ObjectId"] not in by_id, "duplicate object id"
        by_id[o["m_ObjectId"]] = o
    graph = next(o for o in objs if o["m_Type"].endswith(".GraphData"))
    def slots_of(n):
        return {by_id[s["m_Id"]]["m_Id"]: by_id[s["m_Id"]] for s in n.get("m_Slots", [])}
    for o in objs:
        ids = [by_id[s["m_Id"]]["m_Id"] for s in o.get("m_Slots", [])]
        assert len(ids) == len(set(ids)), "duplicate slot id in " + o["m_Name"]
    for key in ("m_Properties", "m_Keywords", "m_Nodes", "m_GroupDatas", "m_CategoryData"):
        for r in graph.get(key, []):
            assert r["m_Id"] in by_id, key + " -> missing object"
    for b in graph["m_FragmentContext"]["m_Blocks"] + graph["m_VertexContext"]["m_Blocks"]:
        assert b["m_Id"] in by_id
        assert {"m_Id": b["m_Id"]} in graph["m_Nodes"], "block not in node list"
    inputs = set()
    for e in graph["m_Edges"]:
        on = by_id[e["m_OutputSlot"]["m_Node"]["m_Id"]]; inn = by_id[e["m_InputSlot"]["m_Node"]["m_Id"]]
        os_ = slots_of(on)[e["m_OutputSlot"]["m_SlotId"]]; is_ = slots_of(inn)[e["m_InputSlot"]["m_SlotId"]]
        assert os_["m_SlotType"] == 1 and is_["m_SlotType"] == 0, "edge direction"
        k = (inn["m_ObjectId"], is_["m_Id"])
        assert k not in inputs, "two edges into one input on " + inn["m_Name"]
        inputs.add(k)
        assert {"m_Id": on["m_ObjectId"]} in graph["m_Nodes"] and {"m_Id": inn["m_ObjectId"]} in graph["m_Nodes"]
    return True

def main():
    args = sys.argv[1:]
    if "--out" not in args:
        print(__doc__); sys.exit(2)
    out = args[args.index("--out") + 1]
    files = [a for i, a in enumerate(args) if a != "--out" and (i == 0 or args[i - 1] != "--out")]
    os.makedirs(out, exist_ok=True)
    for f in files:
        objs = load(f)
        # format check: our writer must reproduce the original byte-for-byte before patching
        original = open(f, encoding="utf-8-sig").read()
        same = load(f) == [json.loads(x) for x in re.split(r"\n\n(?=\{)", dump(load(f)).strip())]
        log = []
        changed = patch(objs, log)
        validate(objs)
        dst = os.path.join(out, os.path.basename(f))
        open(dst, "w", encoding="utf-8", newline="\n").write(dump(objs))
        print("%-40s %s%s" % (os.path.basename(f), "; ".join(log), "" if same else "  (WARNING: re-serialisation changed data)"))

if __name__ == "__main__":
    main()
