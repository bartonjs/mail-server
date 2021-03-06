﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vaettir.Utility;

namespace Vaettir.Mail.Server.FileSystem
{
	public class FileSystemMailStore : IMailStore
	{
		private readonly string _mailDirectory;

		public FileSystemMailStore(string mailDirectory)
		{
			_mailDirectory = mailDirectory;
		}

		private interface IReference
		{
			string Path { get; }
		}

		private class Reference : IMailReference, IReference
		{
			public string Path { get; }

			public Reference(string path)
			{
				Path = path;
			}
		}

		private class ReadReference : IMailReadReference, IReference
		{
			public Stream BodyStream { get; }

			public string Sender { get; }
			public IImmutableList<string> Recipients { get; }
			public string Path { get; }

			public ReadReference(
				string sender,
				IEnumerable<string> recipients,
				string path,
				Stream bodyStream)
			{
				BodyStream = bodyStream;
				Path = path;
				Sender = sender;
				Recipients = ImmutableList.CreateRange(recipients);
			}

			public void Dispose()
			{
				BodyStream?.Dispose();
			}
		}

		private class WriteReference : MailWriteReference, IReference
		{
			public override Stream BodyStream { get; }
			public string Path { get; }

			private readonly string _tempPath;
			private bool _saved = false;

			public WriteReference(string tempPath, string path, string sender, IEnumerable<string> recipients, Stream bodyStream)
				: base(sender, recipients)
			{
				BodyStream = bodyStream;
				_tempPath = tempPath;
				Path = path;
			}

			public override Task SaveAsync(CancellationToken token)
			{
				if (_saved) throw new InvalidOperationException("Already saved");

				BodyStream.Dispose();
				File.Move(_tempPath, Path);
				_saved = true;
				return Task.FromResult((object) null);
			}

			public override void Dispose()
			{
				if (!_saved && File.Exists(_tempPath))
				{
					File.Delete(_tempPath);
				}
			}
		}

		public async Task<IMailWriteReference> NewMailAsync(string sender, IEnumerable<string> recipients, CancellationToken token)
		{
			string mailName = Guid.NewGuid().ToString("D");

			string tempPath = Path.Combine(Path.GetTempPath(), mailName);
			string targetPath = Path.Combine(_mailDirectory, mailName);

			using (var shared = Sharable.Create(File.Create(tempPath)))
			{
				IEnumerable<string> enumerable = recipients as IList<string> ?? recipients.ToList();
				using (var writer = new StreamWriter(shared.Peek(), Encoding.UTF8, 1024, true))
				{
					await writer.WriteLineAsync($"FROM:{sender}");
					foreach (var recipient in enumerable)
					{
						await writer.WriteLineAsync($"TO:{recipient}");
					}
					await writer.WriteLineAsync("--- BEGIN MESSAGE ---");
				}

				return new WriteReference(tempPath, targetPath, sender, enumerable, new OffsetStream(shared.TakeValue()));
			}
		}

		public IEnumerable<IMailReference> GetAllMailReferences()
		{
			return Directory.GetFiles(_mailDirectory, "*", SearchOption.TopDirectoryOnly).Select(path => new Reference(path));
		}

		public async Task<IMailReadReference> OpenReadAsync(IMailReference reference)
		{
			var mailReference = reference as IReference;
			if (mailReference == null)
			{
				throw new ArgumentNullException(nameof(reference));
			}

			using (var stream = Sharable.Create(File.OpenRead(mailReference.Path)))
			{
				string sender;
				List<string> recipients = new List<string>();
				using (var reader = new StreamReader(stream.Peek(), Encoding.UTF8, false, 1024, true))
				{
					var fromLine = await reader.ReadLineAsync();
					if (!fromLine.StartsWith("FROM:"))
					{
						throw new FormatException("Invalid mail file format, expected FROM line");
					}

					sender = fromLine.Substring(5);

					while (true)
					{
						var line = await reader.ReadLineAsync();
						if (line.StartsWith("---"))
						{
							break;
						}

						if (line.StartsWith("TO:"))
						{
							recipients.Add(line.Substring(3));
							continue;
						}

						throw new FormatException("Invalid mail file format, expected TO: line or Begin Message");
					}
				}

				return new ReadReference(sender, recipients, mailReference.Path, stream.TakeValue());
			}
		}

		public Task DeleteAsync(IMailReference reference)
		{
			var mailReference = reference as IReference;
			if (mailReference != null && File.Exists(mailReference.Path))
			{
				File.Delete(mailReference.Path);
			}
			return Task.FromResult((object) null);
		}
	}
}
