using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using ZXing;
using ZXing.QrCode;

namespace EventRegistration
{
    public static class EventRegistration
    {
        [FunctionName("QRGenerator")]
        public static async Task<IActionResult> QrGenerator(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log,
            ExecutionContext context)
        {
            log.LogInformation("QRGenerator API is being used.");

            try
            {
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(context.FunctionAppDirectory)
                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables();
                var config = configBuilder.Build();
            }
            catch (Exception)
            {
                log.LogWarning("Local.Settings.json file could not be read");
            }

            string phone = req.Query["phone"];
            string email = req.Query["email"];
            string blobContainer = req.Query["blobContainer"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            phone = phone ?? data?.phone;
            email = email ?? data?.email;
            blobContainer = blobContainer ?? data?.blobContainer;

            if (phone != null && email != null && blobContainer != null)
            {
                CloudStorageAccount cloudStorageAccount;
                CloudBlobClient cloudBlobClient;
                CloudBlobContainer cloudBlobContainer;
                CloudBlockBlob cloudBlockBlob;

                try
                {
                    log.LogInformation("Connecting to storage account");

                    cloudStorageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));

                    cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();

                    cloudBlobContainer = cloudBlobClient.GetContainerReference(blobContainer);

                    await cloudBlobContainer.CreateIfNotExistsAsync();

                    cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference($"{email} {phone}.svg");
                    cloudBlockBlob.Properties.ContentType = "image/svg+xml";
                }
                catch (Exception e)
                {
                    log.LogError("Could not connect to storage account");
                    return new BadRequestObjectResult(e.Message);
                }

                log.LogInformation("Starting QrCode Configuration");

                ZXing.Rendering.SvgRenderer renderer = new ZXing.Rendering.SvgRenderer();
                var barcodeWriter = new BarcodeWriter<Stream>();
                barcodeWriter.Format = BarcodeFormat.QR_CODE;
                var options = new QrCodeEncodingOptions
                {
                    DisableECI = true,
                    CharacterSet = "UTF-8",
                    Width = 512,
                    Height = 512,
                    Margin = 0
                };
                barcodeWriter.Options = options;

                log.LogInformation("Encoding the data");

                string stringToEncode = String.Format(Environment.GetEnvironmentVariable("URL"), phone);
                log.LogInformation(stringToEncode);
                var imageSVG = renderer.Render(barcodeWriter.Encode(stringToEncode), barcodeWriter.Format, phone);

                log.LogInformation($"Creating image \"{email} {phone}.svg\"");

                var encoding = new UnicodeEncoding();
                byte[] imageBytes = encoding.GetBytes(imageSVG.ToString());
                MemoryStream memoryStream = new MemoryStream(imageBytes);

                try
                {
                    log.LogInformation("Uploading File");
                    await cloudBlockBlob.UploadFromStreamAsync(memoryStream);

                    log.LogInformation("Upload Complete");
                }
                catch (Exception e)
                {
                    log.LogError($"Could not upload image \"{email} {phone}.\" to storage{Environment.NewLine}{e.Message}");
                    return new BadRequestObjectResult($"Could not upload image \"{email} {phone}.svg\" to storage");
                }

                return (ActionResult)new OkObjectResult($"Phone: {phone}{Environment.NewLine}Email: {email}");
            }
            else
            {
                return phone != null || email != null
                    ?
                    (
                        email == null
                        ? new BadRequestObjectResult("Please provide an email address")
                        :
                        (
                            blobContainer == null
                            ? new BadRequestObjectResult("Please provide a BlobContainer")
                            : new BadRequestObjectResult("Please provide a phone number")
                        )
                    )
                    : new BadRequestObjectResult("Please pass a phone number and an email address");
            }
        }

        [FunctionName("GmailEmailSender")]
        public static async Task<IActionResult> GmailEmailSender(
           [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
           ILogger log,
           ExecutionContext context)
        {
            log.LogInformation("EmailSender API has started.");

            try
            {
                log.LogInformation("Reading configuration file");
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(context.FunctionAppDirectory)
                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables();
                var config = configBuilder.Build();
            }
            catch (Exception)
            {
                log.LogWarning("Could not read configuration file");
            }

            string phone = req.Query["phone"];
            string email = req.Query["email"];
            string blobContainer = req.Query["blobContainer"];

            string responseBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(responseBody);

            phone = phone ?? data?.phone;
            email = email ?? data?.email;
            blobContainer = blobContainer ?? data?.blobContainer;

            if (phone != null && email != null && blobContainer != null)
            {
                log.LogInformation("Connection to storage account");

                CloudStorageAccount cloudStorageAccount;
                CloudBlobClient cloudBlobClient;
                CloudBlobContainer cloudBlobContainer;
                CloudBlockBlob cloudBlockBlob;


                try
                {
                    cloudStorageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
                    cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();
                    cloudBlobContainer = cloudBlobClient.GetContainerReference(blobContainer);
                    cloudBlockBlob = cloudBlobContainer.GetBlockBlobReference($"{email} {phone}.svg");
                    log.LogInformation("Connection Established");
                }
                catch (Exception e)
                {
                    log.LogError($"Could not connect to storage account. {e.Message}");
                    return new BadRequestObjectResult($"Could not connect to storage account. {e.Message}");
                }

                log.LogInformation("Obtaining Information From Blob Storage");
                MemoryStream fileStream = new MemoryStream();
                try
                {
                    await cloudBlockBlob.DownloadToStreamAsync(fileStream);
                    log.LogInformation($"Download Complete");
                }
                catch (Exception e)
                {
                    log.LogError($"Could not download data from storage account. {e.Message}");
                    return new BadRequestObjectResult($"Could not download data from storage account. {e.Message}");
                }

                log.LogInformation("Sending email");
                try
                {
                    var mailAddress = new MailAddress(Environment.GetEnvironmentVariable("Email"));
                    var receiverAddress = new MailAddress(email.Split(" ")[0].ToString());
                    string subject = Environment.GetEnvironmentVariable("Subject");
                    string body = Environment.GetEnvironmentVariable("Body");
                    string uri = cloudBlockBlob.Uri.ToString().Replace(" ", "%20");
                    body = String.Format(body, uri);

                    var smtp = new SmtpClient
                    {
                        Host = "smtp.gmail.com",
                        Port = 587,
                        EnableSsl = true,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        UseDefaultCredentials = false,
                        Credentials = new NetworkCredential(mailAddress.Address, Environment.GetEnvironmentVariable("Password"))
                    };

                    using (var mailMessage = new MailMessage(mailAddress, receiverAddress)
                    {
                        Subject = subject,
                        Body = body
                    })

                        smtp.Send(mailMessage);
                }
                catch (Exception e)
                {
                    log.LogError($"Could not send mail. {e.Message}");
                    return new BadRequestObjectResult($"Could not send mail. {e.Message}");
                }

                return new OkObjectResult($"Phone: {phone}{Environment.NewLine}Email: {email}{Environment.NewLine}Blob Container: {blobContainer}");
            }
            else
            {
                return new BadRequestObjectResult($"Please be sure that the following variables have a value: email, phone, blobContainer");
            }
        }

        [FunctionName("StudentRegistration")]
        public static async Task<ActionResult> StudentRegistration(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log,
            ExecutionContext context)
        {

            log.LogInformation("StudentRegistration API Started");

            string phone = req.Query["phone"];
            string email = req.Query["email"];
            string blobContainer = req.Query["blobContainer"];

            string responseBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(responseBody);

            phone = phone ?? data?.phone;
            email = email ?? data?.email;
            blobContainer = blobContainer ?? data?.blobContainer;

            if (phone != null && email != null && blobContainer != null)
            {
                HttpClient httpClient = new HttpClient();

                try
                {
                    log.LogInformation("Reading configuration file");
                    var configBuilder = new ConfigurationBuilder()
                        .SetBasePath(context.FunctionAppDirectory)
                        .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables();
                    var config = configBuilder.Build();
                }
                catch (Exception)
                {
                    log.LogWarning("Could not read configuration file");
                }
                try
                {
                    var content = new StringContent(responseBody.ToString(), Encoding.UTF8, "application/json");
                    httpClient.PostAsync(Environment.GetEnvironmentVariable("QRGenerator"), content).Result.EnsureSuccessStatusCode();
                    httpClient.PostAsync(Environment.GetEnvironmentVariable("GmailEmailSender"), content).Result.EnsureSuccessStatusCode();
                }
                catch (Exception e)
                {
                    log.LogError(e.Message);
                    return new BadRequestObjectResult(e.Message);
                }

                return new OkObjectResult("QR Code and Email were sent");
            }
            else
            {
                return new BadRequestObjectResult($"Please be sure that the following variables have a value: email, phone, blobContainer");
            }

        }
    }
}
