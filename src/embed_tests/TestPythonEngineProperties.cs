using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using Python.Runtime;

namespace Python.EmbeddingTest
{
    public class TestPythonEngineProperties
    {
        [Test]
        public static void GetBuildinfoDoesntCrash()
        {
            using (Py.GIL())
            {
                string s = PythonEngine.BuildInfo;

                Assert.True(s.Length > 5);
                Assert.True(s.Contains(","));
            }
        }

        [Test]
        public static void GetCompilerDoesntCrash()
        {
            using (Py.GIL())
            {
                string s = PythonEngine.Compiler;

                Assert.True(s.Length > 0);
                Assert.True(s.Contains("["));
                Assert.True(s.Contains("]"));
            }
        }

        [Test]
        public static void GetCopyrightDoesntCrash()
        {
            using (Py.GIL())
            {
                string s = PythonEngine.Copyright;

                Assert.True(s.Length > 0);
                Assert.True(s.Contains("Python Software Foundation"));
            }
        }

        [Test]
        public static void GetPlatformDoesntCrash()
        {
            using (Py.GIL())
            {
                string s = PythonEngine.Platform;

                Assert.True(s.Length > 0);
                Assert.True(s.Contains("x") || s.Contains("win"));
            }
        }

        [Test]
        public static void GetVersionDoesntCrash()
        {
            using (Py.GIL())
            {
                string s = PythonEngine.Version;

                Assert.True(s.Length > 0);
                Assert.True(s.Contains(","));
            }
        }

        [Test]
        public static void GetPythonPathDefault()
        {
            PythonEngine.Initialize();
            string s = PythonEngine.PythonPath;

            StringAssert.Contains("python", s.ToLower());
            PythonEngine.Shutdown();
        }

        [Test]
        public static void GetProgramNameDefault()
        {
            PythonEngine.Initialize();
            string s = PythonEngine.PythonHome;

            Assert.NotNull(s);
            PythonEngine.Shutdown();
        }

        /// <summary>
        /// Test default behavior of PYTHONHOME. If ENVVAR is set it will
        /// return the same value. If not, returns EmptyString.
        /// </summary>
        /// <remarks>
        /// AppVeyor.yml has been update to tests with ENVVAR set.
        /// </remarks>
        [Test]
        public static void GetPythonHomeDefault()
        {
            string envPythonHome = Environment.GetEnvironmentVariable("PYTHONHOME") ?? "";

            PythonEngine.Initialize();
            string enginePythonHome = PythonEngine.PythonHome;

            Assert.AreEqual(envPythonHome, enginePythonHome);
            PythonEngine.Shutdown();
        }

        [Test]
        public void SetPythonHome()
        {
            // We needs to ensure that engine was started and shutdown at least once before setting dummy home.
            // Otherwise engine will not run with dummy path with random problem.
            if (!PythonEngine.IsInitialized)
            {
                PythonEngine.Initialize();
            }

            PythonEngine.Shutdown();

            var pythonHomeBackup = PythonEngine.PythonHome;

            var pythonHome = "/dummypath/";

            PythonEngine.PythonHome = pythonHome;
            PythonEngine.Initialize();

            PythonEngine.Shutdown();

            // Restoring valid pythonhome.
            PythonEngine.PythonHome = pythonHomeBackup;
        }

        [Test]
        public void SetPythonHomeTwice()
        {
            // We needs to ensure that engine was started and shutdown at least once before setting dummy home.
            // Otherwise engine will not run with dummy path with random problem.
            if (!PythonEngine.IsInitialized)
            {
                PythonEngine.Initialize();
            }
            PythonEngine.Shutdown();

            var pythonHomeBackup = PythonEngine.PythonHome;

            var pythonHome = "/dummypath/";

            PythonEngine.PythonHome = "/dummypath2/";
            PythonEngine.PythonHome = pythonHome;
            PythonEngine.Initialize();

            Assert.AreEqual(pythonHome, PythonEngine.PythonHome);
            PythonEngine.Shutdown();

            PythonEngine.PythonHome = pythonHomeBackup;
        }

        [Test]
        public void SetProgramName()
        {
            if (PythonEngine.IsInitialized)
            {
                PythonEngine.Shutdown();
            }

            var programNameBackup = PythonEngine.ProgramName;

            var programName = "FooBar";

            PythonEngine.ProgramName = programName;
            PythonEngine.Initialize();

            Assert.AreEqual(programName, PythonEngine.ProgramName);
            PythonEngine.Shutdown();

            PythonEngine.ProgramName = programNameBackup;
        }

        [Test]
        public void SetPythonPath()
        {
            string[] moduleNames = new string[] {
                "subprocess",
            };
            string path;

            using (Py.GIL())
            {
                // path should not be set to PythonEngine.PythonPath here.
                // PythonEngine.PythonPath gets the default module search path, not the full search path.
                // The list sys.path is initialized with this value on interpreter startup;
                // it can be (and usually is) modified later to change the search path for loading modules.
                // See https://docs.python.org/3/c-api/init.html#c.Py_GetPath
                // After PythonPath is set, then PythonEngine.PythonPath will correctly return the full search path. 

                string[] paths = Py.Import("sys").GetAttr("path").As<string[]>();
                path = string.Join(Path.PathSeparator.ToString(), paths);

                TryToImport(moduleNames, "before setting PythonPath");
            }

            PythonEngine.PythonPath = path;

            using (Py.GIL())
            {

                Assert.AreEqual(path, PythonEngine.PythonPath);
                // Check that the modules remain loadable
                TryToImport(moduleNames, "after setting PythonEngine.PythonPath");
            }
        }

        string ListModules()
        {
            try
            {
                var pkg_resources = Py.Import("pkg_resources");
                var locals = new PyDict();
                locals.SetItem("pkg_resources", pkg_resources);
                return PythonEngine.Eval(@"sorted(['%s==%s' % (i.key, i.version) for i in pkg_resources.working_set])", null, locals.Handle).ToString();
            }
            catch (PythonException ex)
            {
                return ex.ToString();
            }
        }

        void CheckImport(string moduleName)
        {
            PythonEngine.Exec($@"
module_name = r'{moduleName}'
import sys
import importlib.util
if module_name not in sys.modules:
    spec = importlib.util.find_spec(module_name)
    if spec is None:
        raise ImportError('find_spec returned None')
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
");
        }

        void TryToImport(IEnumerable<string> moduleNames, string message)
        {
            List<Exception> exceptions = new List<Exception>();
            foreach (var moduleName in moduleNames)
            {
                var exception = TryToImport(moduleName, message);
                if (exception != null) exceptions.Add(exception);
            }
            if (exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }
        }

        Exception TryToImport(string moduleName, string message)
        {
            try
            {
                CheckImport(moduleName);
                Py.Import(moduleName);
                return null;
            }
            catch (PythonException ex)
            {
                string[] paths = Py.Import("sys").GetAttr("path").As<string[]>();
                string path = string.Join(Path.PathSeparator.ToString(), paths);
                string[] messages = paths.Where(p => p.Contains("site-packages")).Select(folder =>
                    (folder != null && Directory.Exists(folder)) ?
                    $" {folder} contains {string.Join(Path.PathSeparator.ToString(), Directory.EnumerateFileSystemEntries(folder).Select(fullName => Path.GetFileName(fullName)).ToArray())}" :
                    "").ToArray();
                string folderContents = string.Join(" ", messages);
                return new Exception($"Py.Import(\"{moduleName}\") failed {message}, sys.path={path}{folderContents} pkg_resources.working_set={ListModules()}", ex);
            }
        }
    }
}
