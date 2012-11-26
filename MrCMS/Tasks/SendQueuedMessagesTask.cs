using System;
using System.Net;
using System.Net.Mail;
using MrCMS.Entities.Messaging;
using MrCMS.Settings;

namespace MrCMS.Tasks
{
    public class SendQueuedMessagesTask : BackgroundTask
    {
        private readonly MailSettings _mailSettings;

        public SendQueuedMessagesTask(MailSettings mailSettings)
        {
            _mailSettings = mailSettings;
        }

        public override void Execute()
        {
            // Set up to use the client from the settings in web.config
            // TODO: move this to admin configured
            using (var smtpClient = new SmtpClient(_mailSettings.Host, _mailSettings.Port)
                                        {
                                            EnableSsl = _mailSettings.UseSSL,
                                            Credentials = new NetworkCredential(_mailSettings.UserName, _mailSettings.Password)
                                        })
            {
                foreach (
                    var queuedMessage in
                        Session.QueryOver<QueuedMessage>().Where(
                            message => message.SentOn == null && message.Tries < MAX_TRIES).List())
                {
                    if (SiteSettings.SiteIsLive)
                        SendMailMessage(queuedMessage, smtpClient);
                    else
                        MarkAsSent(queuedMessage);
                    Session.SaveOrUpdate(queuedMessage);
                }
            }
        }

        private void SendMailMessage(QueuedMessage queuedMessage, SmtpClient smtpClient)
        {
            try
            {
                var mailMessage = new MailMessage(new MailAddress(queuedMessage.FromAddress, queuedMessage.FromName),
                                                  new MailAddress(queuedMessage.ToAddress, queuedMessage.ToName))
                                      {
                                          Subject = queuedMessage.Subject,
                                          Body = queuedMessage.Body
                                      };

                if (!string.IsNullOrWhiteSpace(queuedMessage.Cc))
                    mailMessage.CC.Add(queuedMessage.Cc);
                if (!string.IsNullOrWhiteSpace(queuedMessage.Bcc))
                    mailMessage.Bcc.Add(queuedMessage.Bcc);
                
                foreach (var attachment in queuedMessage.QueuedMessageAttachments)
                    mailMessage.Attachments.Add(new Attachment(attachment.FileName));

                mailMessage.IsBodyHtml = queuedMessage.IsHtml;

                smtpClient.Send(mailMessage);
                MarkAsSent(queuedMessage);
            }
            catch (Exception exception)
            {
                queuedMessage.Tries++;
            }
        }

        private static void MarkAsSent(QueuedMessage queuedMessage)
        {
            queuedMessage.SentOn = DateTime.UtcNow;
        }

        public const int MAX_TRIES = 5;
    }
}