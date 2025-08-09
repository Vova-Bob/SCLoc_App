using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;
using Newtonsoft.Json;

namespace MyApp.Tests
{
    public class BasicTests
    {
        [Fact]
        public void ReadSmallFile()
        {
            var temp = Path.GetTempFileName();
            var expected = "hello";
            File.WriteAllText(temp, expected);
            var text = File.ReadAllText(temp);
            Assert.Equal(expected, text);
            File.Delete(temp);
        }

        private class DummyDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        [Fact]
        public void JsonRoundtrip()
        {
            var dto = new DummyDto { Id = 1, Name = "test" };
            var json = JsonConvert.SerializeObject(dto);
            var back = JsonConvert.DeserializeObject<DummyDto>(json);
            Assert.NotNull(back);
            Assert.Equal(dto.Id, back.Id);
            Assert.Equal(dto.Name, back.Name);
        }

        [Fact]
        public void IniParsingStub()
        {
            var content = "key=value";
            var parts = content.Split('=');
            Assert.Equal("key", parts[0]);
            Assert.Equal("value", parts[1]);
        }

        private static string ResolveConfigPath(string dir)
        {
            var user = Path.Combine(dir, "user.cfg");
            var global = Path.Combine(dir, "global.ini");
            return File.Exists(user) ? user : global;
        }

        [Fact]
        public void ResolveUserConfigPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            var global = Path.Combine(dir, "global.ini");
            File.WriteAllText(global, string.Empty);
            var resolved1 = ResolveConfigPath(dir);
            Assert.Equal(global, resolved1);
            var user = Path.Combine(dir, "user.cfg");
            File.WriteAllText(user, string.Empty);
            var resolved2 = ResolveConfigPath(dir);
            Assert.Equal(user, resolved2);
        }

        [DllImport("kernel32", EntryPoint = "GetTickCount", SetLastError = true)]
        private static extern uint GetTickCount32();

        [DllImport("libc", EntryPoint = "getpid")]
        private static extern int GetPid();

        [Fact]
        public void PInvokeCompiles()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.True(GetTickCount32() > 0);
            }
            else
            {
                Assert.True(GetPid() > 0);
            }
        }
    }
}
