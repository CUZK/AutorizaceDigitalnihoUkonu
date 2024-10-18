using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Xml;

namespace AutorizaceDigitalnihoUkonu.Web.Lib
{
	public static class Extensions
	{
		public static XmlElement ToXmlElement(this string xml)
		{
			var doc = new XmlDocument();
			doc.LoadXml(xml);
			return doc.DocumentElement;
		}

		public static string ToPrettyString(this XmlNode node)
		{
			var sb = new StringBuilder();
			var settings = new XmlWriterSettings
			{
				Indent = true,
				IndentChars = "  ",
				NewLineChars = "\r\n",
				NewLineHandling = NewLineHandling.Replace,
				ConformanceLevel = ConformanceLevel.Auto
			};

			using (var writer = XmlWriter.Create(sb, settings))
			{
				node.WriteTo(writer);
			}

			return sb.ToString();
		}

		public static string GetHash(this HttpPostedFileBase file)
		{
			if (file == null || file.ContentLength == 0)
			{
				throw new ArgumentException("Soubor nebyl vybrán.");
			}

			using (var reader = new BinaryReader(file.InputStream))
			{
				return reader.ReadBytes(file.ContentLength).GetHash("System.Security.Cryptography.SHA256");
			}
		}

		public static string GetHash(this byte[] bytes, string hashName)
		{
			using (var hashAlgorithm = HashAlgorithm.Create(hashName))
			{
				return BitConverter
					.ToString(hashAlgorithm.ComputeHash(bytes))
					.Replace("-", string.Empty)
					.ToLower();
			}
		}
	}
}