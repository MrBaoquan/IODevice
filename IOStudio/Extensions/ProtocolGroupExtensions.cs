using System.Collections.Generic;
using System.Linq;
using IOStudio.Models;

namespace IOStudio.Extensions
{
    /// <summary>
    /// ProtocolGroup 与 ProtocolGroupDto 之间的映射扩展方法
    /// </summary>
    public static class ProtocolGroupExtensions
    {
        /// <summary>
        /// 将 ProtocolGroup（UI模型）转换为 ProtocolGroupDto（序列化模型）
        /// </summary>
        public static ProtocolGroupDto ToDto(this ProtocolGroup group)
        {
            return new ProtocolGroupDto
            {
                Id = group.Id,
                GroupName = group.GroupName,
                ProtocolType = group.ProtocolType,
                TargetIP = group.TargetIP,
                TargetPort = group.TargetPort,
                SerialPort = group.SerialPort,
                BaudRate = group.BaudRate,
                DataBits = group.DataBits,
                Parity = group.Parity,
                StopBits = group.StopBits,
                Description = group.Description,
                IsExpanded = group.IsExpanded,
                IsCustomMode = group.IsCustomMode,
                CustomProtocolType = group.CustomProtocolType,
                SourceDevice = group.SourceDevice,
                TargetDevice = group.TargetDevice,
                Mappings = group.Mappings.Select(m => m.Clone()).ToList()
            };
        }

        /// <summary>
        /// 将 ProtocolGroupDto（序列化模型）转换为 ProtocolGroup（UI模型）
        /// </summary>
        public static ProtocolGroup ToModel(this ProtocolGroupDto dto)
        {
            var group = new ProtocolGroup
            {
                Id = dto.Id,
                GroupName = dto.GroupName,
                ProtocolType = dto.ProtocolType,
                TargetIP = dto.TargetIP,
                TargetPort = dto.TargetPort,
                SerialPort = dto.SerialPort,
                BaudRate = dto.BaudRate,
                DataBits = dto.DataBits,
                Parity = dto.Parity,
                StopBits = dto.StopBits,
                Description = dto.Description,
                IsExpanded = dto.IsExpanded,
                IsCustomMode = dto.IsCustomMode,
                CustomProtocolType = dto.CustomProtocolType,
                SourceDevice = dto.SourceDevice,
                TargetDevice = dto.TargetDevice
            };

            if (dto.Mappings != null)
            {
                foreach (var mapping in dto.Mappings)
                {
                    group.Mappings.Add(mapping.Clone());
                }
            }

            return group;
        }

        /// <summary>
        /// 批量转换为 DTO 列表
        /// </summary>
        public static List<ProtocolGroupDto> ToDtoList(this IEnumerable<ProtocolGroup> groups)
        {
            return groups.Select(g => g.ToDto()).ToList();
        }

        /// <summary>
        /// 批量转换为 Model 列表
        /// </summary>
        public static List<ProtocolGroup> ToModelList(this IEnumerable<ProtocolGroupDto> dtos)
        {
            return dtos.Select(d => d.ToModel()).ToList();
        }

        /// <summary>
        /// 克隆 MappingDto
        /// </summary>
        public static MappingDto Clone(this MappingDto mapping)
        {
            return new MappingDto
            {
                SourceKey = mapping.SourceKey,
                TargetKey = mapping.TargetKey,
                IsEnabled = mapping.IsEnabled,
                Description = mapping.Description,
                DataFormat = mapping.DataFormat,
                PressedData = mapping.PressedData,
                ReleasedData = mapping.ReleasedData,
                Type = mapping.Type
            };
        }
    }
}
