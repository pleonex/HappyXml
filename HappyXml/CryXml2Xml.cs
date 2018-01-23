//
// CryXmlConverter.cs
//
// Author:
//       Benito Palacios Sanchez <benito356@gmail.com>
//
// Copyright (c) 2018 Benito Palacios Sanchez
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System.Collections.Generic;
using System.Linq;
namespace HappyXml
{
    using System;
    using System.Text;
    using System.Xml.Linq;
    using Yarhl.FileFormat;
    using Yarhl.IO;

    public class CryXml2Xml : IConverter<BinaryFormat, XDocument>
    {
        public XDocument Convert(BinaryFormat source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            XDocument xml = new XDocument(new XDeclaration("1.0", "utf-8", "true"));
            DataReader reader = new DataReader(source.Stream) {
                DefaultEncoding = Encoding.UTF8
            };

            Header header = ReadHeader(reader);

            // Add first node since it's not in the hierarchy list
            NodeInfo root = ReadNodeInfo(0, reader, header);
            xml.Add(NodeToXml(root, reader, header));

            // Use a queue to count number of children and recreate hierarchy
            var queue = new Queue<Tuple<NodeInfo, XElement>>();
            queue.Enqueue(new Tuple<NodeInfo, XElement>(root, xml.Root));

            // Iterate over the hierarchy table
            while (queue.Count > 0) {
                var parent = queue.Dequeue();
                var node = parent.Item1;
                var xele = parent.Item2;

                for (int i = 0; i < node.ChildrenNumber; i++) {
                    // Get children
                    reader.Stream.Seek(header.NodeHierarchyOffset + (node.FirstChildrenReference + i) * 4, SeekMode.Start);
                    NodeInfo child = ReadNodeInfo(reader.ReadInt32(), reader, header);

                    var xchild = NodeToXml(child, reader, header);
                    xele.Add(xchild);

                    if (child.ChildrenNumber > 0)
                        queue.Enqueue(new Tuple<NodeInfo, XElement>(child, xchild));
                }
            }

            return xml;
        }

        static Header ReadHeader(DataReader reader)
        {
            Header header = new Header();

            header.Stamp = reader.ReadString(8);
            if (header.Stamp != "CryXmlB\0")
                throw new FormatException("Unknown stamp");

            header.FileSize = reader.ReadUInt32() & 0x3FFFFFFF; // unknown 2 bits

            header.NodeInfoOffset = reader.ReadUInt32();
            header.NodeInfoCount = reader.ReadUInt32();

            header.AttributesOffset = reader.ReadUInt32();
            header.AttributesCount = reader.ReadUInt32();

            header.NodeHierarchyOffset = reader.ReadUInt32();
            header.NodeHierarchyCount = reader.ReadUInt32();

            header.StringListOffset = reader.ReadUInt32();
            header.StringListCount = reader.ReadUInt32();

            header.Unknown1 = reader.ReadUInt16();
            header.Unknown2 = reader.ReadUInt16();

            header.Offset5 = reader.ReadUInt32();
            header.Offset6 = reader.ReadUInt32();
            header.Offset7 = reader.ReadUInt32();
            header.Offset8 = reader.ReadUInt32();

            header.Unknown3 = reader.ReadUInt32();

            return header;
        }

        static Attribute ReadAttribute(uint number, DataReader reader, Header header)
        {
            reader.Stream.PushToPosition(header.AttributesOffset + (number * 8), SeekMode.Start);
            Attribute attr = new Attribute {
                Name = ReadString(reader.ReadUInt32(), reader, header),
                Value = ReadString(reader.ReadUInt32(), reader, header)
            };
            reader.Stream.PopPosition();

            return attr;
        }

        static NodeInfo ReadNodeInfo(int number, DataReader reader, Header header)
        {
            reader.Stream.PushToPosition(header.NodeInfoOffset + (number * 0x1C), SeekMode.Start);
            NodeInfo node = new NodeInfo {
                TagName = ReadString(reader.ReadUInt32(), reader, header),
                Value = ReadString(reader.ReadUInt32(), reader, header),

                AttributesNumber = reader.ReadUInt32(),
                ChildrenNumber = reader.ReadUInt32(),
                ParentTagNumber = reader.ReadUInt32(),
                FirstAttributeId = reader.ReadUInt32(),
                FirstChildrenReference = reader.ReadUInt32()
            };
            reader.Stream.PopPosition();

            return node;
        }

        static XElement NodeToXml(NodeInfo node, DataReader reader, Header header)
        {
            XNamespace xmlns = string.Empty;
            List<XAttribute> attributes = new List<XAttribute>();
            for (uint i = 0; i < node.AttributesNumber; i++) {
                var attr = ReadAttribute(node.FirstAttributeId + i, reader, header);
                if (attr.Name == "xmlns")
                    xmlns = attr.Value;
                else {
                    XName name;
                    var thename = attr.Name.Split(':');
                    if (thename.Length == 2)
                        name = XName.Get(thename[1], thename[0]);
                    else
                        name = attr.Name;
                    attributes.Add(new XAttribute(name, attr.Value));
                }
            }

            XElement element = new XElement(xmlns + node.TagName, attributes);
            if (!string.IsNullOrEmpty(node.Value))
                element.Value = node.Value;

            return element;
        }

        static string ReadString(uint offset, DataReader reader, Header header)
        {
            reader.Stream.PushToPosition(header.StringListOffset + offset, SeekMode.Start);
            string val = reader.ReadString();
            reader.Stream.PopPosition();

            return val;
        }

        struct Header
        {
            public string Stamp { get; set; }

            public uint FileSize { get; set; }

            public uint NodeInfoOffset { get; set; }
            public uint NodeInfoCount { get; set; }

            public uint AttributesOffset { get; set; }
            public uint AttributesCount { get; set; }

            public uint NodeHierarchyOffset { get; set; }
            public uint NodeHierarchyCount { get; set; }

            public uint StringListOffset { get; set; }
            public uint StringListCount { get; set; }

            public ushort Unknown1 { get; set; }
            public ushort Unknown2 { get; set; }

            public uint Offset5 { get; set; }
            public uint Offset6 { get; set; }
            public uint Offset7 { get; set; }
            public uint Offset8 { get; set; }

            public uint Unknown3 { get; set; }
        }

        struct Attribute
        {
            public string Name { get; set; }

            public string Value { get; set; }

            public override string ToString()
            {
                return string.Format("[Attribute: Name={0}, Value={1}]", Name, Value);
            }
        }

        struct NodeInfo
        {
            public string TagName { get; set; }

            public string Value { get; set; }

            public uint AttributesNumber { get; set; }

            public uint ChildrenNumber { get; set; }

            public uint ParentTagNumber { get; set; }

            public uint FirstAttributeId { get; set; }

            public uint FirstChildrenReference { get; set; }

            public override string ToString()
            {
                return string.Format("[NodeInfo:" +
                                     "\n\tTagName={0}," +
                                     "\n\tValue={1}," +
                                     "\n\tAttributesNumber={2}," +
                                     "\n\tChildrenNumber={3}," +
                                     "\n\tParentTagNumber={4}," +
                                     "\n\tFirstAttributeId={5}," +
                                     "\n\tFirstChildrenReference={6:X8}]",
                                     TagName, Value, AttributesNumber, ChildrenNumber, ParentTagNumber, FirstAttributeId, FirstChildrenReference);
            }
        }
    }
}
