using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.Json;
using Amazon.S3;
using Amazon.S3.Model;
using HotelManager.Models;
using HttpMultipartParser;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
namespace HotelManager
{
    public class HotelAdmin
    {
        public async Task<APIGatewayProxyResponse> ListHotels(APIGatewayProxyRequest request)
        {
            // query string parameter called token is passed to this lambda method.

            var response = new APIGatewayProxyResponse
            {
                Headers = new Dictionary<string, string>(),
                StatusCode = 200
            };

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Headers", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "OPTIONS,GET");
            response.Headers.Add("Content-Type", "application/json");

            if (request?.QueryStringParameters == null)
            {
                Console.WriteLine(
                    "Query string is null. You must configure the Query String Mapping in your API resource in API Gateway");
                return response;
            }

            var token = request.QueryStringParameters.ContainsKey("token") ? request.QueryStringParameters["token"] : "";
            if (string.IsNullOrEmpty(token))
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Body = JsonConvert.SerializeObject(new { Error = "Query parameter 'token' not present." });
                return response;
            }


            var tokenDetails = new JwtSecurityToken(token);
            var userId = tokenDetails.Claims.FirstOrDefault(x => x.Type == "sub")?.Value;

            var region = Environment.GetEnvironmentVariable("AWS_REGION");
            var dbClient = new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(region));
            using var dbContext = new DynamoDBContext(dbClient);

            var hotels = await dbContext.ScanAsync<Hotel>(new[] { new ScanCondition("UserId", ScanOperator.Equal, userId) })
                .GetRemainingAsync();

            response.Body = JsonConvert.SerializeObject(new { Hotels = hotels });

            return response;
        }

        public async Task<APIGatewayProxyResponse> AddHotel(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var response = new APIGatewayProxyResponse()
            {
                Headers = new Dictionary<string, string>(),
                StatusCode = 200,
            };

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Headers", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "OPTIONS.POST");
            response.Headers.Add("Content-Type", "application/json");

            if (request.Body == null)
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Body = JsonConvert.SerializeObject(new { Error = "Couldn't find a valid hotel information in the request" });
                return response;
            }

            var bodyContent = request.IsBase64Encoded
                ? Convert.FromBase64String(request.Body)
                : Encoding.UTF8.GetBytes(request.Body);

            await using var memStream = new MemoryStream(bodyContent);
            var formData = await MultipartFormDataParser.ParseAsync(memStream).ConfigureAwait(false);

            var hotelName = formData.GetParameterValue(name: "name");
            var hotelRating = formData.GetParameterValue("rating");
            var hotelCity = formData.GetParameterValue("city");
            var hotelPrice = formData.GetParameterValue("price");

            var file = formData.Files.FirstOrDefault();
            string fileName;
            if (file != null)
            {
                fileName = file.FileName;
            }
            else
            {
                fileName = "Empty";
            }

            await using var fileContentStream = new MemoryStream();
            await file.Data.CopyToAsync(fileContentStream);
            fileContentStream.Position = 0;

            var userId = formData.GetParameterValue("userId");
            var idToken = formData.GetParameterValue("idToken");

            var token = new JwtSecurityToken(idToken);
            var group = token.Claims.FirstOrDefault(x => x.Type == "cognito:groups");
            if (group == null || group.Value != "hotel-manager")
            {
                Console.WriteLine("Group Value: " + group.Value);
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                response.Body = JsonConvert.SerializeObject(new { Error = "Unauthorized.Must be a member of admin group" });
                return response;
            }

            var region = Environment.GetEnvironmentVariable("AWS_REGION");
            var bucketName = Environment.GetEnvironmentVariable("bucketName");

            var client = new AmazonS3Client(RegionEndpoint.GetBySystemName(region));
            var dbClient = new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(region));

            try
            {
                await client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = fileName,
                    InputStream = fileContentStream,
                    AutoCloseStream = true
                });

                var hotel = new Hotel
                {
                    UserId = userId,
                    Id = Guid.NewGuid().ToString(),
                    Name = hotelName,
                    City = hotelCity,
                    Price = int.Parse(hotelPrice),
                    Rating = int.Parse(hotelRating),
                    FileName = fileName,
                };

                using var dbContext = new DynamoDBContext(dbClient);
                await dbContext.SaveAsync(hotel);
            }
            catch (Exception)
            {

                throw;
            }

            Console.WriteLine("Done.");
            return response;
        }
    }
}