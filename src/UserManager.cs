using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EasyConsole;
using Futronic.Devices.FS80;
using SourceAFIS;

namespace Futronic.Devices.FS80
{
    class UserManagerProgram : Program
    {
        public static string PrintsFolderName = "Prints";

        static void Main(string[] args)
        {
            // Create directory for prints
            Directory.CreateDirectory(PrintsFolderName);

            new UserManagerProgram().Run();
        }

        public UserManagerProgram() : base(nameof(Futronic), true)
        {
            AddPage(new Welcome(this));
            AddPage(new UserList(this));
            AddPage(new CreateUser(this));
            AddPage(new UserDetails(this));
            AddPage(new AddFingerprint(this));
            AddPage(new IdentifyUser(this));



            SetPage<Welcome>();
        }

        public string SelectedUser { get; set; }

        public static IEnumerable<string> GetUsernames()
        {
            var users = Directory.GetDirectories(PrintsFolderName);

            foreach (var directory in users)
            {
                var username = directory.Substring(PrintsFolderName.Length + 1);

                yield return username;
            }
        }

        internal class Welcome : MenuPage
        {
            public Welcome(Program program) : base(nameof(Welcome), program)
            {
                this.Menu.Add("Show list of known Users", () => program.NavigateTo<UserList>());
                this.Menu.Add("Identify User by scanning fingerprint", () => program.NavigateTo<IdentifyUser>());

                this.Menu.Add("Exit", () => Environment.Exit(0));
            }
        }

        internal class UserList : MenuPage
        {
            private readonly Program _program;

            public UserList(Program program) : base("Current List of users", program)
            {
                _program = program;
            }

            public override void Display()
            {
                foreach (var username in GetUsernames())
                {
                    var menuItem = $"User '{username}'";

                    if (!this.Menu.Contains(menuItem))
                    {
                        this.Menu.Add(menuItem, () =>
                        {
                            ((UserManagerProgram) _program).SelectedUser = username;
                            this.Program.NavigateTo<UserDetails>();
                        });
                    }
                }

                var createNew = "Create new user";

                if (!this.Menu.Contains(createNew))
                {
                    this.Menu.Add(createNew, () => _program.NavigateTo<CreateUser>());
                }

                base.Display();
            }
        }

        internal class UserDetails : MenuPage
        {
            public UserDetails(Program program) : base(nameof(UserDetails), program)
            {
                this.Menu.Add("Add Fingerprint", () => program.NavigateTo<AddFingerprint>());
            }
        }

        internal class CreateUser : Page
        {
            public CreateUser(Program program) : base("Create new user", program)
            {
            }

            public override void Display()
            {
                base.Display();

                var username = Input.ReadString("Please provide a username: ");

                Directory.CreateDirectory(Path.Combine(UserManagerProgram.PrintsFolderName, username));

                Output.WriteLine(ConsoleColor.Green, $"User '{username}' added", username);

                Input.ReadString("Press enter to continue");

                this.Program.NavigateBack();
            }
        }

        internal class AddFingerprint : Page
        {
            public AddFingerprint(Program program) : base(nameof(AddFingerprint), program)
            {
            }

            public override void Display()
            {
                base.Display();

                var device = new DeviceAccessor().AccessFingerprintDevice();

                device.FingerDetected += (sender, args) => { HandleNewFingerprint(device.ReadFingerprint()); };

                device.StartFingerDetection();

                Output.WriteLine("Please place your finger on the device or press enter to cancel");

                Input.ReadString(string.Empty);
                device.Dispose();

                this.Program.NavigateBack();
            }

            private void HandleNewFingerprint(Bitmap bitmap)
            {
                Output.WriteLine("Finger detected. Saving...");

                var randomFilename = Path.GetRandomFileName().Replace('.', 'f') + ".bmp";
                var username = ((UserManagerProgram) this.Program).SelectedUser;

                bitmap.Save(Path.Combine(UserManagerProgram.PrintsFolderName, username, randomFilename));

                Output.WriteLine(ConsoleColor.DarkGreen, "Fingerprint registered");
            }
        }

        internal class IdentifyUser : Page
        {

            public IdentifyUser(Program program) : base(nameof(IdentifyUser), program)
            {
            }

            public override void Display()
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
                    }

                    allPersons.Add(person);
                }


                var device = new DeviceAccessor().AccessFingerprintDevice();

                device.FingerDetected += (sender, args) =>
                {
                    device.StopFingerDetection();

                    Output.WriteLine("Finger detected, dont remove");
                    var readFingerprint = device.ReadFingerprint();

                    Output.WriteLine("Finger captured. Validation in progress");
                    ValidateFingerprint(readFingerprint, allPersons);

                    device.StartFingerDetection();
                };

                device.StartFingerDetection();

                Output.WriteLine("Please place your finger on the device or press enter to cancel");

                Input.ReadString(string.Empty);
                device.Dispose();

                this.Program.NavigateBack();

            }

            private void ValidateFingerprint(Bitmap bitmap, List<Person> allPersons)
            {
                ImageConverter imageConverter = new();
                
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

                    Output.WriteLine(ConsoleColor.DarkGreen, $"Matched with {user}!");
                }
                else
                    Output.WriteLine(ConsoleColor.DarkRed, "No match!");
            }
        } 
    }
}
