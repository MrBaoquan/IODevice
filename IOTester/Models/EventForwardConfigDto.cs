using System.Collections.Generic;
using System.Xml.Serialization;
using ReactiveUI;

namespace IOTester.Models
{
    public enum MappingType
    {
        Digital,
        Analog
    }

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

        [XmlAttribute]
        public bool IsCustomMode { get; set; } = false;

        [XmlAttribute]
        public string CustomProtocolType { get; set; } = "TCP-Client";

        [XmlAttribute]
        public string SourceDevice { get; set; } = "";

        [XmlAttribute]
        public string TargetDevice { get; set; } = "";

        [XmlArray("Mappings")]
        [XmlArrayItem("Mapping")]
        public List<MappingDto> Mappings { get; set; } = new List<MappingDto>();
    }

    public class MappingDto : ReactiveObject
    {
        private string _sourceKey = "";
        private string _targetKey = "";
        private bool _isEnabled = true;
        private string _description = "";
        private string _dataFormat = "ASCII";
        private string _pressedData = "";
        private string _releasedData = "";
        private string _type = "Digital";

        [XmlAttribute]
        public string SourceKey
        {
            get => _sourceKey;
            set => this.RaiseAndSetIfChanged(ref _sourceKey, value);
        }

        [XmlAttribute]
        public string TargetKey
        {
            get => _targetKey;
            set => this.RaiseAndSetIfChanged(ref _targetKey, value);
        }

        [XmlAttribute]
        public bool IsEnabled
        {
            get => _isEnabled;
            set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
        }

        [XmlAttribute]
        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        [XmlAttribute]
        public string DataFormat
        {
            get => _dataFormat;
            set => this.RaiseAndSetIfChanged(ref _dataFormat, value);
        }

        [XmlAttribute]
        public string PressedData
        {
            get => _pressedData;
            set => this.RaiseAndSetIfChanged(ref _pressedData, value);
        }

        [XmlAttribute]
        public string ReleasedData
        {
            get => _releasedData;
            set => this.RaiseAndSetIfChanged(ref _releasedData, value);
        }

        [XmlAttribute]
        public string Type
        {
            get => _type;
            set => this.RaiseAndSetIfChanged(ref _type, value);
        }

        [XmlIgnore]
        public MappingType MappingType
        {
            get => Type == "Analog" ? MappingType.Analog : MappingType.Digital;
            set => Type = value == MappingType.Analog ? "Analog" : "Digital";
        }
    }
}
