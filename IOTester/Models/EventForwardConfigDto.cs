using System.Collections.Generic;
using System.Xml.Serialization;

namespace IOTester.Models
{
    [XmlRoot("EventForwardConfig")]
    public class EventForwardConfigDto
    {
        [XmlArray("ProtocolGroups")]
        [XmlArrayItem("ProtocolGroup")]
        public List<ProtocolGroupDto> ProtocolGroups { get; set; } = new List<ProtocolGroupDto>();
    }

    public class ProtocolGroupDto
    {
        [XmlAttribute]
        public string Id { get; set; } = "";

        [XmlAttribute]
        public string GroupName { get; set; } = "";

        [XmlAttribute]
        public string ProtocolType { get; set; } = "NetIO";

        [XmlAttribute]
        public string TargetIP { get; set; } = "127.0.0.1";

        [XmlAttribute]
        public int TargetPort { get; set; } = 8000;

        [XmlAttribute]
        public string SerialPort { get; set; } = "COM1";

        [XmlAttribute]
        public int BaudRate { get; set; } = 9600;

        [XmlAttribute]
        public int DataBits { get; set; } = 8;

        [XmlAttribute]
        public string Parity { get; set; } = "None";

        [XmlAttribute]
        public int StopBits { get; set; } = 1;

        [XmlAttribute]
        public string Description { get; set; } = "";

        [XmlAttribute]
        public bool IsExpanded { get; set; } = true;

        [XmlArray("Mappings")]
        [XmlArrayItem("Mapping")]
        public List<MappingDto> Mappings { get; set; } = new List<MappingDto>();
    }

    public class MappingDto
    {
        [XmlAttribute]
        public string SourceDevice { get; set; } = "";

        [XmlAttribute]
        public string SourceKey { get; set; } = "";

        [XmlAttribute]
        public string TargetKey { get; set; } = "";

        [XmlAttribute]
        public bool IsEnabled { get; set; } = true;

        [XmlAttribute]
        public string Description { get; set; } = "";
    }
}
