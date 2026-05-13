using System.IO;
using NUnit.Framework;

namespace Approov.Tests
{
    public class ApproovNativeBridgeSourceTests
    {
        [Test]
        public void AndroidBridgeClass_AttachesCurrentThreadForEveryAccess()
        {
            string source = ReadPackageFile("Runtime/Approov/ApproovBridge.cs");
            int propertyIndex = source.IndexOf("private static AndroidJavaClass BridgeClass", System.StringComparison.Ordinal);
            int attachIndex = source.IndexOf("AndroidJNI.AttachCurrentThread();", propertyIndex, System.StringComparison.Ordinal);
            int lockIndex = source.IndexOf("lock (BridgeClassLock)", propertyIndex, System.StringComparison.Ordinal);
            int createIndex = source.IndexOf("sBridgeClass = new AndroidJavaClass", propertyIndex, System.StringComparison.Ordinal);

            Assert.That(propertyIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(attachIndex, Is.GreaterThan(propertyIndex));
            Assert.That(lockIndex, Is.GreaterThan(attachIndex));
            Assert.That(createIndex, Is.GreaterThan(lockIndex));
        }

        [Test]
        public void IosNullableStateMethods_ForwardNilToSdk()
        {
            string source = ReadPackageFile("Plugins/iOS/ApproovBridge-ObjectiveC.mm");

            StringAssert.Contains(
                "NSString *propertyString = property == NULL ? nil : [NSString stringWithUTF8String:property];",
                source);
            StringAssert.Contains("[Approov setUserProperty:propertyString];", source);
            StringAssert.Contains(
                "NSString *keyString = key == NULL ? nil : [NSString stringWithUTF8String:key];",
                source);
            StringAssert.Contains("[Approov setDevKey:keyString];", source);
            StringAssert.Contains(
                "NSString *dataString = data == NULL ? nil : [NSString stringWithUTF8String:data];",
                source);
            StringAssert.Contains("[Approov setDataHashInToken:dataString];", source);
        }

        private static string ReadPackageFile(string relativePath)
        {
            foreach (string root in CandidateRoots())
            {
                string path = Path.Combine(root, relativePath);
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }

            Assert.Fail("Unable to locate package file " + relativePath);
            return null;
        }

        private static string[] CandidateRoots()
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            return new[]
            {
                currentDirectory,
                Path.Combine(currentDirectory, "Packages/io.approov.service.unity"),
                Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../..")),
                Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../Packages/io.approov.service.unity")),
            };
        }
    }
}
