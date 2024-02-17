﻿using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.Interop;
using Glamourer.Interop.Structs;
using Newtonsoft.Json;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Files;

namespace Glamourer.Interop.Material;

[JsonConverter(typeof(Converter))]
public readonly record struct MaterialValueIndex(
    MaterialValueIndex.DrawObjectType DrawObject,
    byte SlotIndex,
    byte MaterialIndex,
    byte RowIndex)
{
    public static readonly MaterialValueIndex Invalid = new(DrawObjectType.Invalid, 0, 0, 0);

    public uint Key
        => ToKey(DrawObject, SlotIndex, MaterialIndex, RowIndex);

    public bool Valid
        => Validate(DrawObject) && ValidateSlot(SlotIndex) && ValidateMaterial(MaterialIndex) && ValidateRow(RowIndex);

    public static bool FromKey(uint key, out MaterialValueIndex index)
    {
        index = new MaterialValueIndex(key);
        return index.Valid;
    }

    public static MaterialValueIndex FromSlot(EquipSlot slot)
    {
        if (slot is EquipSlot.MainHand)
            return new MaterialValueIndex(DrawObjectType.Mainhand, 0, 0, 0);
        if (slot is EquipSlot.OffHand)
            return new MaterialValueIndex(DrawObjectType.Offhand, 0, 0, 0);

        var idx = slot.ToIndex();
        if (idx < 10)
            return new MaterialValueIndex(DrawObjectType.Human, (byte)idx, 0, 0);

        return Invalid;
    }

    public EquipSlot ToSlot()
        => DrawObject switch
        {
            DrawObjectType.Human when SlotIndex < 10    => ((uint)SlotIndex).ToEquipSlot(),
            DrawObjectType.Mainhand when SlotIndex == 0 => EquipSlot.MainHand,
            DrawObjectType.Offhand when SlotIndex == 0  => EquipSlot.OffHand,
            _                                           => EquipSlot.Unknown,
        };

    public unsafe bool TryGetModel(Actor actor, out Model model)
    {
        if (!actor.Valid)
        {
            model = Model.Null;
            return false;
        }

        model = DrawObject switch
        {
            DrawObjectType.Human    => actor.Model,
            DrawObjectType.Mainhand => actor.IsCharacter ? actor.AsCharacter->DrawData.WeaponDataSpan[0].DrawObject : Model.Null,
            DrawObjectType.Offhand  => actor.IsCharacter ? actor.AsCharacter->DrawData.WeaponDataSpan[1].DrawObject : Model.Null,
            _                       => Model.Null,
        };
        return model.IsCharacterBase;
    }

    public unsafe bool TryGetTextures(Actor actor, out ReadOnlySpan<Pointer<Texture>> textures)
    {
        if (!TryGetModel(actor, out var model)
         || SlotIndex >= model.AsCharacterBase->SlotCount
         || model.AsCharacterBase->ColorTableTexturesSpan.Length < (SlotIndex + 1) * MaterialService.MaterialsPerModel)
        {
            textures = [];
            return false;
        }

        textures = model.AsCharacterBase->ColorTableTexturesSpan.Slice(SlotIndex * MaterialService.MaterialsPerModel,
            MaterialService.MaterialsPerModel);
        return true;
    }

    public unsafe bool TryGetTexture(Actor actor, out Texture** texture)
    {
        if (TryGetTextures(actor, out var textures))
            return TryGetTexture(textures, out texture);

        texture = null;
        return false;
    }

    public unsafe bool TryGetTexture(ReadOnlySpan<Pointer<Texture>> textures, out Texture** texture)
    {
        if (MaterialIndex >= textures.Length || textures[MaterialIndex].Value == null)
        {
            texture = null;
            return false;
        }

        fixed (Pointer<Texture>* ptr = textures)
        {
            texture = (Texture**)ptr + MaterialIndex;
        }

        return true;
    }

    public unsafe bool TryGetColorTable(Actor actor, out MtrlFile.ColorTable table)
    {
        if (TryGetTexture(actor, out var texture))
            return TryGetColorTable(texture, out table);

        table = default;
        return false;
    }

    public unsafe bool TryGetColorTable(Texture** texture, out MtrlFile.ColorTable table)
        => DirectXTextureHelper.TryGetColorTable(*texture, out table);

    public bool TryGetColorRow(Actor actor, out MtrlFile.ColorTable.Row row)
    {
        if (!TryGetColorTable(actor, out var table))
        {
            row = default;
            return false;
        }

        row = table[RowIndex];
        return true;
    }


    public static MaterialValueIndex FromKey(uint key)
        => new(key);

    public static MaterialValueIndex Min(DrawObjectType drawObject = 0, byte slotIndex = 0, byte materialIndex = 0, byte rowIndex = 0)
        => new(drawObject, slotIndex, materialIndex, rowIndex);

    public static MaterialValueIndex Max(DrawObjectType drawObject = (DrawObjectType)byte.MaxValue, byte slotIndex = byte.MaxValue,
        byte materialIndex = byte.MaxValue, byte rowIndex = byte.MaxValue)
        => new(drawObject, slotIndex, materialIndex, rowIndex);

    public enum DrawObjectType : byte
    {
        Invalid,
        Human,
        Mainhand,
        Offhand,
    };

    public static bool Validate(DrawObjectType type)
        => type is not DrawObjectType.Invalid && Enum.IsDefined(type);

    public static bool ValidateSlot(byte slotIndex)
        => slotIndex < 10;

    public static bool ValidateMaterial(byte materialIndex)
        => materialIndex < MaterialService.MaterialsPerModel;

    public static bool ValidateRow(byte rowIndex)
        => rowIndex < MtrlFile.ColorTable.NumRows;

    private static uint ToKey(DrawObjectType type, byte slotIndex, byte materialIndex, byte rowIndex)
    {
        var result = (uint)rowIndex;
        result |= (uint)materialIndex << 8;
        result |= (uint)slotIndex << 16;
        result |= (uint)((byte)type << 24);
        return result;
    }

    private MaterialValueIndex(uint key)
        : this((DrawObjectType)(key >> 24), (byte)(key >> 16), (byte)(key >> 8), (byte)key)
    { }

    public override string ToString()
    {
        var slot = ToSlot();
        return slot is EquipSlot.Unknown
            ? $"{DrawObject} Slot {SlotIndex} Material #{MaterialIndex + 1} Row #{RowIndex + 1}" 
            : $"{slot.ToName()} Material #{MaterialIndex + 1} Row #{RowIndex + 1}";
    }

    private class Converter : JsonConverter<MaterialValueIndex>
    {
        public override void WriteJson(JsonWriter writer, MaterialValueIndex value, JsonSerializer serializer)
            => serializer.Serialize(writer, value.Key);

        public override MaterialValueIndex ReadJson(JsonReader reader, Type objectType, MaterialValueIndex existingValue, bool hasExistingValue,
            JsonSerializer serializer)
            => FromKey(serializer.Deserialize<uint>(reader), out var value) ? value : throw new Exception($"Invalid material key {value.Key}.");
    }
}