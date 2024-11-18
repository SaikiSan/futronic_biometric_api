using System;
using System.Collections.Generic;
using System.Drawing;
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

    [HttpPost]
    public async Task<IActionResult> RegisterUser(UserViewModel person)
    {
        var userFolder = Path.Combine(PrintsFolderName, person.Name);
        Directory.CreateDirectory(userFolder);

        using var device = new DeviceAccessor().AccessFingerprintDevice();
        var tcs = new TaskCompletionSource<bool>();

        try
        {
            device.FingerDetected += (sender, args) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    device.StopFingerDetection();
                    var fingerprint = device.ReadFingerprint();
                    HandleNewFingerprint(fingerprint, person);
                    tcs.SetResult(true);
                }
            };

            device.StartFingerDetection();

            var taskCompleted = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));

            if (taskCompleted != tcs.Task)
            {
                return StatusCode(408, "Fingerprint detection timed out.");
            }

            return Ok();
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
                    result =  ValidateFingerprint(readFingerprint, users);

                    device.StartFingerDetection();
                    tcs.SetResult(true);
                }
            };

            device.StartFingerDetection();

            var taskCompleted = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));

            if (taskCompleted != tcs.Task)
            {
                return StatusCode(408, "Fingerprint detection timed out.");
            }
            if(result.ContainsValue(true))
                return Ok(result.Keys);
            else
                return BadRequest(result.Keys);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred: {ex.Message}");
        }
    }

    private void HandleNewFingerprint(Bitmap bitmap, UserViewModel person)
    {
        var randomFilename = Path.GetRandomFileName().Replace('.', 'f') + ".bmp";
        var userFolder = Path.Combine(PrintsFolderName, person.Name);

        bitmap.Save(Path.Combine(userFolder, randomFilename));
    }

    private static IEnumerable<Person> GetUserList()
    {
        ImageConverter imageConverter = new();
        var allPersons = new List<Person>();
        var i = 0;
        // Create missing templates
        foreach (var username in GetUsernames())
        {
            var person = new Person();
            person.Id = i++;
            var dataFolder = Path.Combine(PrintsFolderName, username);
            var allBitmaps = Directory.GetFiles(dataFolder, "*.bmp", SearchOption.TopDirectoryOnly).Select(Path.GetFileName);
            foreach (var bitmapFile in allBitmaps)
            {
                Bitmap bitmap = new Bitmap(Path.Combine(dataFolder, bitmapFile));
                    person.Fingerprints.Add(new FingerprintTemplate(
                        new FingerprintImage((byte[])imageConverter.ConvertTo(bitmap, typeof(byte[])))));
            
                allPersons.Add(person);
            }
        }

        return allPersons;
    }

    private static IEnumerable<string> GetUsernames()
    {
        var users = Directory.GetDirectories(PrintsFolderName);

        foreach (var directory in users)
        {
            var username = directory.Substring(PrintsFolderName.Length + 1);
            yield return username;
        }
    }

    private static Dictionary<string, bool> ValidateFingerprint(Bitmap bitmap, IEnumerable<Person> allPersons)
    {
        ImageConverter imageConverter = new();
        var result = new Dictionary<string, bool>();
        
        var probe = new FingerprintTemplate(
            new FingerprintImage((byte[])imageConverter.ConvertTo(bitmap, typeof(byte[]))));
        
        var matcher = new FingerprintMatcher(probe);
        Person match = null;
        double max = Double.NegativeInfinity;
        foreach (var candidate in allPersons)
        {
            foreach(var fingerPrint in candidate.Fingerprints)
            {
                double similarity = matcher.Match(fingerPrint);
                if (similarity > max)
                {
                    max = similarity;
                    match = candidate;
                }
            }
        }
        double threshold = 40;
        if(max >= threshold && match != null)
        {
            var user = GetUsernames().ToList().ElementAt(match.Id);
            
            result[$"Matched with {user}!"] = true;
            
            return  result;
        }
        else
        {
            result["No match!"] = false;

            return  result;
        }
    }
}