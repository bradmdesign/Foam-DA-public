"""Verified native Pane rectangle hook for selected top-level game windows.

Positions and native hit targets move together. The payload is position
independent and occupies checked, unused .text padding; no runtime writer.
"""
import re
import struct

POPUPS = (
    "MailListDialog", "MailDialog", "NewMailDialog", "BoardListDialog", "ArticleListDialog",
    "ArticleDialog", "NewArticleDialog", "GUIOptionPane", "OptionPane",
    "ShowUsersPane", "ShowUsersPane_A", "_ShowUsersPane@GUIBackPane",
    "FriendListDialog", "GameSettingDialog", "MacroDialog", "HotKeyPane",
    "GroupListPane", "GroupAdDialogPane", "GroupAdInfoDialogPane",
    "FieldMapPane", "TownMapPane",
)


def primary_vtable(pe, data, name):
    base = pe.OPTIONAL_HEADER.ImageBase
    marker = (".?AV" + name + "@@\0").encode()
    pos = data.find(marker)
    if pos < 8 or data.find(marker, pos + 1) >= 0:
        raise ValueError("Missing/ambiguous RTTI: " + name)
    descriptor = base + pe.get_rva_from_offset(pos - 8)
    candidates = []
    for match in re.finditer(re.escape(struct.pack("<I", descriptor)), data):
        col = match.start() - 12
        if col < 0 or data[col:col + 12] != bytes(12):
            continue
        address = base + pe.get_rva_from_offset(col)
        for ref in re.finditer(re.escape(struct.pack("<I", address)), data):
            vtable = base + pe.get_rva_from_offset(ref.start() + 4)
            fn = struct.unpack_from("<I", data, ref.start() + 4)[0]
            if base + 0x1000 <= fn < base + 0x268000:
                candidates.append(vtable)
    if len(candidates) != 1:
        raise ValueError("Ambiguous primary vtable for " + name + ": " + repr(candidates))
    return candidates[0]


# Reserved zero-initialized space immediately after the original .data extent.
FRAME_SEQUENCE_RVA = 0x4e241c


def wrapper(rva, tables, width, height):
    code = bytearray()
    labels, branches = {}, []
    def emit(value): code.extend(bytes.fromhex(value))
    def word(value): code.extend(struct.pack("<I", value & 0xffffffff))
    def branch(opcode, target):
        emit(opcode); branches.append((len(code), target)); word(0)
    # Save this and EBX. A compact RVA table leaves room for the frame counter.
    emit("55 8bec 83ec10 51 53 e8 00000000 5a 81ea"); word(rva+13)
    emit("8b01 2bc2 81c2"); table_at=len(code); word(0)
    emit("b9"); word(len(tables))
    labels["lookup"]=len(code)
    emit("3b02"); branch("0f84","placed")
    emit("83c204 49"); branch("0f85","lookup")
    emit("5b 59 c9"); branch("e9","original")
    labels["placed"]=len(code)
    emit("8bd9 8b5508") # final two table entries are centered maps
    for low,high,extent,margin,out_low,out_high in (
        (0,8,height,169,0xf0,0xf8),(4,12,width,22,0xf4,0xfc)
    ):
        emit("8b42");code.append(high);emit("2b42");code.append(low)
        emit("b9");word(extent);emit("2bc8 83fb02")
        branch("0f87","anchor"+str(low))
        emit("d1f9");branch("e9","clamp"+str(low))
        labels["anchor"+str(low)]=len(code);emit("81e9");word(margin)
        labels["clamp"+str(low)]=len(code)
        emit("85c9 7902 33c9 894d");code.append(out_low)
        emit("03c1 8945");code.append(out_high)
    emit("8b4dec 8d45f0 50");branch("e8","original")
    emit("5b c9 c20400")
    struct.pack_into("<I",code,table_at,rva+len(code))
    for table in tables:word(table)
    labels["original"]=len(code)
    emit("55 8bec 83ec30 e9");word(0x149eb6-(rva+len(code)+4))
    for at,target in branches:struct.pack_into("<i",code,at,labels[target]-(at+4))
    return bytes(code)


def sequence_hook(rva, target, finish=False):
    """Bracket native world update AND composition; preserve native return state."""
    code=bytearray()
    def call(dest):
        code.extend(b"\xe8"+struct.pack("<i",dest-(rva+len(code)+5)))
    if finish:call(target)
    code.extend(bytes.fromhex("9c 50 e8 00000000 58 81c0"))
    code.extend(struct.pack("<i",FRAME_SEQUENCE_RVA-(rva+len(code)-3)))
    code.extend(bytes.fromhex("ff00 58 9d"))
    if finish:code.append(0xc3)
    else:code.extend(b"\xe9"+struct.pack("<i",target-(rva+len(code)+5)))
    return bytes(code)


def install(pe, original, width, height, patch, changes):
    section = pe.sections[0]
    if section.Name.rstrip(b"\0") != b".text":
        raise ValueError("Unexpected code section")
    cave = (section.Misc_VirtualSize + 15) & ~15
    rva = section.VirtualAddress + cave
    tables = [primary_vtable(pe, original, name) - pe.OPTIONAL_HEADER.ImageBase for name in POPUPS]
    payload = wrapper(rva, tables, width, height)
    begin_rva=rva+len(payload)
    payload += sequence_hook(begin_rva,0x152820)
    end_rva=rva+len(payload)
    payload += sequence_hook(end_rva,0x4b1b0,True)
    for at,target,replacement in ((0x5540d3,0x552820,begin_rva),(0x55413e,0x44b1b0,end_rva)):
        patch(at,b"\xe8"+struct.pack("<i",target-at-5),
              b"\xe8"+struct.pack("<i",replacement-(at-0x400000)-5),
              "Frame sequence brackets native world update and final composition")
    state=pe.sections[2]
    if state.VirtualAddress+state.Misc_VirtualSize!=FRAME_SEQUENCE_RVA or FRAME_SEQUENCE_RVA+4>=pe.sections[3].VirtualAddress:
        raise ValueError("Unexpected frame sequence storage extent")
    changes.append({"file_offset":hex(state.get_field_absolute_offset("Misc_VirtualSize")),
                    "original":struct.pack("<I",state.Misc_VirtualSize).hex(),
                    "replacement":struct.pack("<I",state.Misc_VirtualSize+4).hex(),
                    "purpose":"Frame sequence zero-initialized counter after original data"})
    if cave + len(payload) > section.SizeOfRawData:
        raise ValueError("Popup wrapper exceeds verified code padding")
    at = section.PointerToRawData + cave
    if original[at:at + len(payload)] != bytes(len(payload)):
        raise ValueError("Popup code padding is occupied")
    patch(pe.OPTIONAL_HEADER.ImageBase + rva, bytes(len(payload)), payload,
          "Native popup placement beside right controls; travel/town maps centered")
    patch(0x549eb0, bytes.fromhex("55 8bec 83ec30"),
          b"\xe9" + struct.pack("<i", rva - 0x149eb5) + b"\x90",
          "Route native Pane rectangle placement through the popup allowlist")
    # The cave fits the existing raw section; explicitly include it in VS.
    offset = section.get_field_absolute_offset("Misc_VirtualSize")
    changes.append({"file_offset": hex(offset), "original": struct.pack("<I", section.Misc_VirtualSize).hex(),
                    "replacement": struct.pack("<I", cave + len(payload)).hex(),
                    "purpose": "Include popup centering code in the existing executable section"})
    return offset, struct.pack("<I", cave + len(payload))
