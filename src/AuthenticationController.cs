using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BiometricDevices;
using Microsoft.AspNetCore.Mvc;
using SourceAFIS;

namespace API_Finger;

[ApiController]
[Route("api/[controller]")]
public class AuthenticationController : ControllerBase
{
    private const string PrintsFolderName = "Prints";
    private const int DetectionTimeoutSeconds = 10;
    private const double MatchThreshold = 40;

    public AuthenticationController()
    {
        if (!Directory.Exists(PrintsFolderName))
            Directory.CreateDirectory(PrintsFolderName);
    }

    [HttpPost]
    public async Task<IActionResult> RegisterUser(UserViewModel person)
    {
        var users = GetUserList();

        using var device = new DeviceAccessor().AccessFingerprintDevice();
        var tcs = new TaskCompletionSource<bool>();
        bool isFingerprintDuplicate = false;
        try
        {
            device.FingerDetected += (sender, args) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    device.StopFingerDetection();
                    var fingerprint = device.ReadFingerprint();

                    if (IsFingerprintDuplicate(fingerprint, users))
                    {
                        isFingerprintDuplicate = true;
                    }
                    else
                    {
                        SaveFingerprint(fingerprint, person);
                    }
                    if(!tcs.Task.IsCompleted)
                        tcs.SetResult(true);
                }
            };

            device.StartFingerDetection();
            var taskCompleted = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(DetectionTimeoutSeconds)));

            if (taskCompleted != tcs.Task)
                return StatusCode(408, "Fingerprint detection timed out.");
            
            if (isFingerprintDuplicate)
                return BadRequest("Fingerprint already registered.");

            return Ok("User registered successfully.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred: {ex.Message}");
        }
    }

    [HttpGet]
    public async Task<IActionResult> ValidateFinger()
    {
        var users = GetUserList();
        var result = new Dictionary<string, bool>();

        using var device = new DeviceAccessor().AccessFingerprintDevice();
        var tcs = new TaskCompletionSource<bool>();

        try
        {
            device.FingerDetected += (sender, args) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    device.StopFingerDetection();
                    var readFingerprint = device.ReadFingerprint();
                    result = MatchFingerprint(readFingerprint, users);
                    if(!tcs.Task.IsCompleted)
                        tcs.SetResult(true);
                }
            };

            device.StartFingerDetection();
            var taskCompleted = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(DetectionTimeoutSeconds)));

            if (taskCompleted != tcs.Task)
                return StatusCode(408, "Fingerprint detection timed out.");

            var match = result.FirstOrDefault(r => r.Value);
            if (match.Value)
                return Ok($"Fingerprint matched with: {match.Key}");

            return BadRequest("No match found.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred: {ex.Message}");
        }
    }

    private void SaveFingerprint(Bitmap bitmap, UserViewModel person)
    {
        var userFolder = Path.Combine(PrintsFolderName, person.Name);
        Directory.CreateDirectory(userFolder);

        var randomFilename = Path.GetRandomFileName().Replace('.', 'f') + ".bmp";
        bitmap.Save(Path.Combine(userFolder, randomFilename));
    }

    private static bool IsFingerprintDuplicate(Bitmap fingerprint, IEnumerable<Person> users)
    {
        return MatchFingerprint(fingerprint, users).Any(result => result.Value);
    }

    private static Dictionary<string, bool> MatchFingerprint(Bitmap bitmap, IEnumerable<Person> allPersons)
    {
        ImageConverter imageConverter = new();
        var probe = new FingerprintTemplate(
            new FingerprintImage((byte[])imageConverter.ConvertTo(bitmap, typeof(byte[]))));

        var matcher = new FingerprintMatcher(probe);
        var matches = new Dictionary<string, bool>();

        foreach (var person in allPersons)
        {
            foreach (var fingerprint in person.Fingerprints)
            {
                if (matcher.Match(fingerprint) >= MatchThreshold)
                {
                    matches[person.Name] = true;
                    return matches;
                }
            }
        }

        matches["No match"] = false;
        return matches;
    }

    private static IEnumerable<Person> GetUserList()
    {
        var persons = new List<Person>();
        var directories = Directory.GetDirectories(PrintsFolderName);

        ImageConverter imageConverter = new();
        int id = 0;

        foreach (var directory in directories)
        {
            var person = new Person
            {
                Id = id++,
                Name = Path.GetFileName(directory)
            };

            var bitmapFiles = Directory.GetFiles(directory, "*.bmp");

            foreach (var file in bitmapFiles)
            {
                Bitmap bitmap = new Bitmap(file);
                var fingerprintTemplate = new FingerprintTemplate(
                    new FingerprintImage((byte[])imageConverter.ConvertTo(bitmap, typeof(byte[]))));
                person.Fingerprints.Add(fingerprintTemplate);
            }
            persons.Add(person);
        }
        return persons;
    }
}
