using System.Diagnostics;
using NUnit.Framework;

namespace ch_ircbot
{
    [TestFixture]
    public class TuidTestFixture
    {
        [Test]
        public void TestTuid()
        {
            for (int i = 0; i < 16; ++i)
            {
                var tuid = Tuid.New();
                Debug.WriteLine(tuid);
                Assert.That(Tuid.Valid(tuid));
                Assert.That(!Tuid.Valid("SNHdh3ZaaiWZbphyNv3twO6tMri2PgbHdB"));
            }
        }
    }
}