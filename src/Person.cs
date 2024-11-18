using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SourceAFIS;

namespace Futronic.Devices.FS80;

public class Person
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<FingerprintTemplate> Fingerprints { get; set; } = [];
}