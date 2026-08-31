using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "Usage: CommunityPatchVersionPatcher <Magicka.exe> <old> <new>");
    return 2;
}

string path = Path.GetFullPath(args[0]);
string oldVersion = args[1];
string newVersion = args[2];
if (oldVersion.Length != newVersion.Length)
{
    throw new InvalidOperationException(
        "The old and new patch versions must have the same UTF-16 length.");
}

byte[] image = ReadAllBytesWithRetry(path);
VersionLocation location = FindVersionLocation(image);
if (location.Value == newVersion)
{
    Console.WriteLine(
        "Magicka.exe already references patch version " + newVersion + ".");
    return 0;
}
if (location.Value != oldVersion)
{
    throw new InvalidDataException(
        "CommunityPatchInfo.Version references '" + location.Value
        + "', expected '" + oldVersion + "'.");
}

byte[] oldBytes = Encoding.Unicode.GetBytes(oldVersion);
byte[] newBytes = Encoding.Unicode.GetBytes(newVersion);
if (!image.AsSpan(location.FileOffset, oldBytes.Length).SequenceEqual(oldBytes))
{
    throw new InvalidDataException(
        "The active #US heap entry does not contain the expected UTF-16 bytes.");
}
newBytes.CopyTo(image, location.FileOffset);
WriteAllBytesWithRetry(path, image);

VersionLocation verified = FindVersionLocation(ReadAllBytesWithRetry(path));
if (verified.Value != newVersion)
{
    throw new InvalidDataException(
        "Version patch verification failed; active value is '"
        + verified.Value + "'.");
}

Console.WriteLine(
    "Updated active CommunityPatchInfo.Version: " + oldVersion + " -> "
    + newVersion + " at file offset 0x" + location.FileOffset.ToString("X")
    + ".");
return 0;

static VersionLocation FindVersionLocation(byte[] image)
{
    using MemoryStream stream = new MemoryStream(image, writable: false);
    using PEReader pe = new PEReader(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    TypeDefinitionHandle typeHandle = metadata.TypeDefinitions.Single(handle =>
    {
        TypeDefinition type = metadata.GetTypeDefinition(handle);
        return metadata.GetString(type.Namespace) == "Magicka.CommunityPatch"
            && metadata.GetString(type.Name) == "CommunityPatchInfo";
    });
    TypeDefinition typeDefinition = metadata.GetTypeDefinition(typeHandle);
    MethodDefinitionHandle methodHandle = typeDefinition.GetMethods().Single(
        handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name)
            == "get_Version");
    MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
    MethodBodyBlock body = pe.GetMethodBody(method.RelativeVirtualAddress);
    byte[] il = body.GetILBytes()
        ?? throw new InvalidDataException(
            "CommunityPatchInfo.Version has no IL body.");
    int[] ldstrOffsets = Enumerable.Range(0, Math.Max(0, il.Length - 4))
        .Where(index => il[index] == 0x72)
        .ToArray();
    if (ldstrOffsets.Length != 1)
    {
        throw new InvalidDataException(
            "CommunityPatchInfo.Version must contain exactly one ldstr.");
    }

    int token = BinaryPrimitives.ReadInt32LittleEndian(
        il.AsSpan(ldstrOffsets[0] + 1, 4));
    if ((token & unchecked((int)0xFF000000)) != 0x70000000)
    {
        throw new InvalidDataException(
            "CommunityPatchInfo.Version ldstr does not reference the #US heap.");
    }
    UserStringHandle handle = MetadataTokens.UserStringHandle(
        token & 0x00FFFFFF);
    string value = metadata.GetUserString(handle);

    int metadataRootOffset = RvaToFileOffset(
        pe.PEHeaders,
        pe.PEHeaders.CorHeader?.MetadataDirectory.RelativeVirtualAddress
            ?? throw new InvalidDataException("The CLI metadata directory is missing."));
    int userStringHeapOffset = FindStreamOffset(
        image,
        metadataRootOffset,
        "#US");
    int entryOffset = metadataRootOffset + userStringHeapOffset
        + MetadataTokens.GetHeapOffset(handle);
    (int byteCount, int prefixLength) = ReadCompressedInteger(image, entryOffset);
    int stringByteCount = byteCount - 1;
    if (stringByteCount < 0 || (stringByteCount & 1) != 0)
    {
        throw new InvalidDataException("The active #US entry has an invalid size.");
    }
    int fileOffset = entryOffset + prefixLength;
    string rawValue = Encoding.Unicode.GetString(
        image,
        fileOffset,
        stringByteCount);
    if (rawValue != value)
    {
        throw new InvalidDataException(
            "The decoded #US entry does not match MetadataReader.");
    }
    return new VersionLocation(value, fileOffset);
}

static int RvaToFileOffset(PEHeaders headers, int rva)
{
    foreach (SectionHeader section in headers.SectionHeaders)
    {
        int size = Math.Max(section.VirtualSize, section.SizeOfRawData);
        if (rva >= section.VirtualAddress
            && rva < section.VirtualAddress + size)
        {
            return section.PointerToRawData + rva - section.VirtualAddress;
        }
    }
    throw new InvalidDataException(
        "RVA 0x" + rva.ToString("X") + " is outside all PE sections.");
}

static int FindStreamOffset(byte[] image, int rootOffset, string streamName)
{
    if (BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(rootOffset, 4)) != 0x424A5342)
    {
        throw new InvalidDataException("The CLI metadata signature is invalid.");
    }
    int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
        image.AsSpan(rootOffset + 12, 4));
    int position = Align4(rootOffset + 16 + versionLength);
    ushort streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
        image.AsSpan(position + 2, 2));
    position += 4;
    for (int index = 0; index < streamCount; index++)
    {
        int offset = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(position, 4));
        position += 8;
        int nameStart = position;
        while (position < image.Length && image[position] != 0)
        {
            position++;
        }
        if (position >= image.Length)
        {
            throw new InvalidDataException("A metadata stream name is invalid.");
        }
        string name = Encoding.ASCII.GetString(
            image,
            nameStart,
            position - nameStart);
        position = Align4(position + 1);
        if (name == streamName)
        {
            return offset;
        }
    }
    throw new InvalidDataException("Metadata stream " + streamName + " is missing.");
}

static (int Value, int Length) ReadCompressedInteger(byte[] image, int offset)
{
    byte first = image[offset];
    if ((first & 0x80) == 0)
    {
        return (first, 1);
    }
    if ((first & 0xC0) == 0x80)
    {
        return (((first & 0x3F) << 8) | image[offset + 1], 2);
    }
    if ((first & 0xE0) == 0xC0)
    {
        return (
            ((first & 0x1F) << 24)
            | (image[offset + 1] << 16)
            | (image[offset + 2] << 8)
            | image[offset + 3],
            4);
    }
    throw new InvalidDataException("Invalid compressed integer in #US heap.");
}

static int Align4(int value) => (value + 3) & ~3;

static byte[] ReadAllBytesWithRetry(string path)
{
    for (int attempt = 1; ; attempt++)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (IOException) when (attempt < 20)
        {
            Thread.Sleep(250);
        }
    }
}

static void WriteAllBytesWithRetry(string path, byte[] image)
{
    for (int attempt = 1; ; attempt++)
    {
        try
        {
            File.WriteAllBytes(path, image);
            return;
        }
        catch (IOException) when (attempt < 20)
        {
            Thread.Sleep(250);
        }
    }
}

internal sealed record VersionLocation(string Value, int FileOffset);
