using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BWPackedXmlReader
{
    /// <summary>
    /// PackedSection files are read-only data sections described in Backus–Naur form (BNF) format.
    /// </summary>
    public class BWPackedXml
    {
        private static readonly char[] intToBase64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/".ToCharArray();
        private const int PACKED_HEADER = 0x62A14E45;

        public XDocument Document { get; private set; } = new XDocument();

        public override string? ToString()
        {
            return Document?.ToString();
        }

        public BWPackedXml(string filepath, ReaderSettings? settings = null)
        {
            settings ??= new ReaderSettings();

            using FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read);
            {
                Read(fs, settings, filepath);
            }
        }

        public BWPackedXml(byte[] buffer, ReaderSettings? settings = null)
        {
            settings ??= new ReaderSettings();

            using MemoryStream ms = new MemoryStream(buffer);
            {
                Read(ms, settings);
            }
        }

        private void Read(Stream stream, ReaderSettings settings, string? filepath = null)
        {
            using BinaryReader binaryReader = new BinaryReader(stream);
            int head = binaryReader.ReadInt32();

            if (head == PACKED_HEADER)
            {
                binaryReader.ReadSByte(); // version

                List<string> dictionary = ReadDictionary(binaryReader);

                XElement root = new XElement(settings.RootName);

                ReadElement(binaryReader, root, dictionary);

                Document.Add(root);

                if (settings.FixUnnamedValues)
                {
                    FixUnnamedValues(Document);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(filepath))
                {
                    throw new FileLoadException($"Buffer does not contain a Packed Xml!");
                }
                else
                {
                    throw new FileLoadException($"File \"{Path.GetFileName(filepath)}\" is not a Packed Xml!");
                }                
            }
        }

        private void FixUnnamedValues(XDocument doc)
        {
            foreach (var element in doc.Descendants())
            {
                if (element.HasElements && element.FirstNode.GetType() == typeof(XText))
                {
                    var textNode = element.Nodes().OfType<XText>().FirstOrDefault();
                    var val = textNode.Value;

                    var newEl = new XElement("value");
                    newEl.Value = val;
                    textNode.ReplaceWith(newEl);
                }
            }
        }

        private List<string> ReadDictionary(BinaryReader binaryReader)
        {
            List<string> dictionary = new List<string>();
            int counter = 0;
            string text = ReadStringTillZero(binaryReader);

            while (!(text.Length == 0))
            {
                dictionary.Add(text);
                text = ReadStringTillZero(binaryReader);
                counter += 1;
            }
            return dictionary;
        }

        private string ReadStringTillZero(BinaryReader reader)
        {
            List<char> chars = new List<char>(255);

            char c = reader.ReadChar();

            while (c != Convert.ToChar(0x0))
            {
                chars.Add(c);
                c = reader.ReadChar();
            }

            return new string(chars.ToArray());
        }

        private void ReadElement(BinaryReader reader, XElement element, List<string> dictionary)
        {
            int childrenNmber = ReadLittleEndianShort(reader);
            DataDescriptor selfDataDescriptor = ReadDataDescriptor(reader);
            ElementDescriptor[] children = ReadElementDescriptors(reader, childrenNmber);

            int offset = ReadData(reader, dictionary, element, 0, selfDataDescriptor);

            foreach (var elementDescriptor in children)
            {
                XElement child = new XElement(dictionary[elementDescriptor.nameIndex]);

                offset = ReadData(reader, dictionary, child, offset, elementDescriptor.dataDescriptor);

                element.Add(child);
            }
        }

        private int ReadData(BinaryReader reader, List<string> dictionary, XElement element, int offset, DataDescriptor dataDescriptor)
        {
            int lengthInBytes = dataDescriptor.end - offset;

            if (dataDescriptor.type == 0x0)
            {
                ReadElement(reader, element, dictionary);
            }
            else if (dataDescriptor.type == 0x1)
            {
                element.Value = ReadString(reader, lengthInBytes);
            }
            else if (dataDescriptor.type == 0x2)
            {
                element.Value = ReadNumber(reader, lengthInBytes);
            }
            else if (dataDescriptor.type == 0x3)
            {
                string str = ReadFloats(reader, lengthInBytes);
                string[] strData = str.Split(' ');
                if (strData.Length == 12)
                {
                    XElement row0 = new XElement("row0");
                    XElement row1 = new XElement("row1");
                    XElement row2 = new XElement("row2");
                    XElement row3 = new XElement("row3");

                    row0.Value = strData[0] + " " + strData[1] + " " + strData[2];
                    row1.Value = strData[3] + " " + strData[4] + " " + strData[5];
                    row2.Value = strData[6] + " " + strData[7] + " " + strData[8];
                    row3.Value = strData[9] + " " + strData[10] + " " + strData[11];

                    element.Add(row0);
                    element.Add(row1);
                    element.Add(row2);
                    element.Add(row3);
                }
                else
                {
                    element.Value = str;
                }
            }
            else if (dataDescriptor.type == 0x4)
            {
                if (ReadBoolean(reader, lengthInBytes))
                {
                    element.Value = "true";
                }
                else
                {
                    element.Value = "false";
                }
            }
            else if (dataDescriptor.type == 0x5)
            {
                element.Value = ReadBase64(reader, lengthInBytes);
            }
            else
            {
                throw new System.ArgumentException("Unknown type of " + element.Name + ": " + dataDescriptor.ToString() + " " + ReadAndToHex(reader, lengthInBytes));
            }

            return dataDescriptor.end;
        }

        private short ReadLittleEndianShort(BinaryReader reader)
        {
            short LittleEndianShort = reader.ReadInt16();
            return LittleEndianShort;
        }

        private int ReadLittleEndianInt(BinaryReader reader)
        {
            int LittleEndianInt = reader.ReadInt32();
            return LittleEndianInt;
        }

        private long ReadLittleEndianlong(BinaryReader reader)
        {
            long LittleEndianlong = reader.ReadInt64();
            return LittleEndianlong;
        }

        private DataDescriptor ReadDataDescriptor(BinaryReader reader)
        {
            int selfEndAndType = ReadLittleEndianInt(reader);
            return new DataDescriptor(Convert.ToInt32(selfEndAndType & 0xFFFFFFF), selfEndAndType >> 28, Convert.ToInt32(reader.BaseStream.Position));
        }

        private ElementDescriptor[] ReadElementDescriptors(BinaryReader reader, int number)
        {
            ElementDescriptor[] elements = new ElementDescriptor[number]; // -1

            for (int i = 0; i <= number - 1; i++)
            {
                int nameIndex = ReadLittleEndianShort(reader);
                DataDescriptor dataDescriptor = ReadDataDescriptor(reader);
                elements[i] = new ElementDescriptor(nameIndex, dataDescriptor);
            }

            return elements;
        }

        private string ReadString(BinaryReader reader, int lengthInBytes)
        {
            //string rString = new string(reader.ReadChars(lengthInBytes), 0, lengthInBytes);
            string rString = Encoding.UTF8.GetString(reader.ReadBytes(lengthInBytes));
            return rString;
        }

        private string ReadNumber(BinaryReader reader, int lengthInBytes)
        {
            string number = "";
            switch (lengthInBytes)
            {
                case 1:
                    number = Convert.ToString(reader.ReadSByte());
                    break;
                case 2:
                    number = Convert.ToString(ReadLittleEndianShort(reader));
                    break;
                case 4:
                    number = Convert.ToString(ReadLittleEndianInt(reader));
                    break;
                case 8:
                    number = Convert.ToString(ReadLittleEndianlong(reader));
                    break;
                default:
                    number = "0";
                    break;
            }

            return number;
        }

        private float ReadLittleEndianFloat(BinaryReader reader)
        {
            float LittleEndianFloat = reader.ReadSingle();
            return LittleEndianFloat;
        }

        private string ReadFloats(BinaryReader reader, int lengthInBytes)
        {
            int n = lengthInBytes / 4;

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i <= n - 1; ++i)
            {
                if (i != 0)
                {
                    sb.Append(" ");
                }

                float rFloat = ReadLittleEndianFloat(reader);
                sb.Append(rFloat.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private bool ReadBoolean(BinaryReader reader, int lengthInBytes)
        {
            bool result = lengthInBytes == 1;
            if (result)
            {
                if (reader.ReadSByte() != 1)
                {
                    throw new System.ArgumentException("Boolean error");
                }
            }

            return result;
        }

        private string ReadBase64(BinaryReader reader, int lengthInBytes)
        {
            sbyte[] bytes = new sbyte[lengthInBytes];  // -1

            for (int i = 0; i <= lengthInBytes - 1; ++i)
            {
                bytes[i] = reader.ReadSByte();
            }

            return ByteArrayToBase64(bytes);
        }

        private string ByteArrayToBase64(sbyte[] a)
        {
            int aLen = a.Length;
            int numFullGroups = aLen / 3;
            int numBytesInPartialGroup = aLen - 3 * numFullGroups;
            int resultLen = 4 * ((aLen + 2) / 3);

            var result = new StringBuilder(resultLen);

            int inCursor = -1;

            for (int i = 0; i < numFullGroups; i++)
            {
                int byte0 = a[++inCursor] & 0xFF;
                int byte1 = a[++inCursor] & 0xFF;
                int byte2 = a[++inCursor] & 0xFF;

                result.Append(intToBase64[byte0 >> 2]);
                result.Append(intToBase64[(byte0 << 4) & 0x3F | (byte1 >> 4)]);

                char base64Char1 = intToBase64[(byte1 << 2) & 0x3F | (byte2 >> 6)];
                char base64Char2 = intToBase64[byte2 & 0x3F];

                result.Append(base64Char1);
                result.Append(base64Char2);
            }

            switch (numBytesInPartialGroup)
            {
                case 1:
                    int byte0 = a[++inCursor] & 0xFF;
                    result.Append(intToBase64[byte0 >> 2]);
                    result.Append(intToBase64[(byte0 << 4) & 0x3F]);
                    result.Append("==");
                    break;

                case 2:
                    byte0 = a[++inCursor] & 0xFF;
                    int byte1 = a[++inCursor] & 0xFF;
                    result.Append(intToBase64[byte0 >> 2]);
                    result.Append(intToBase64[(byte0 << 4) & 0x3F | (byte1 >> 4)]);
                    result.Append(intToBase64[(byte1 << 2) & 0x3F]);
                    result.Append('=');
                    break;
            }

            return result.ToString();
        }

        private string ReadAndToHex(BinaryReader reader, int lengthInBytes)
        {
            sbyte[] bytes = new sbyte[lengthInBytes]; // -1

            for (int i = 0; i <= lengthInBytes - 1; ++i)
            {
                bytes[i] = reader.ReadSByte();
            }

            StringBuilder sb = new StringBuilder("[ ");

            foreach (var b in bytes)
            {
                sb.Append(Convert.ToString((b & 0xFF), 16));
                sb.Append(" ");
            }

            sb.Append("]L:");
            sb.Append(lengthInBytes);

            return sb.ToString();
        }

        private class ElementDescriptor
        {
            public readonly int nameIndex;
            public readonly DataDescriptor dataDescriptor;

            public ElementDescriptor(int nameIndex, DataDescriptor dataDescriptor)
            {
                this.nameIndex = nameIndex;
                this.dataDescriptor = dataDescriptor;
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder("[");

                sb.Append("0x");
                sb.Append(Convert.ToString(nameIndex, 16));
                sb.Append(":");
                sb.Append(dataDescriptor);

                return sb.ToString();
            }
        }

        private class DataDescriptor
        {
            public readonly int address;
            public readonly int end;
            public readonly int type;

            public DataDescriptor(int end, int type, int address)
            {
                this.end = end;
                this.type = type;
                this.address = address;
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder("[");

                sb.Append("0x");
                sb.Append(Convert.ToString(end, 16));
                sb.Append(", ");
                sb.Append("0x");
                sb.Append(Convert.ToString(type, 16));
                sb.Append("]@0x");
                sb.Append(Convert.ToString(address, 16));

                return sb.ToString();
            }
        }

        public class ReaderSettings
        {
            public string RootName { get; set; } = "packedSection";
            public bool FixUnnamedValues { get; set; } = false;
        }
    }
}
