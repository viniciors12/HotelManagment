using Amazon.DynamoDBv2.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotelManager.Models
{
    [DynamoDBTable("Hotels")]
    public class Hotel
    {
        [DynamoDBHashKey]
        public string? UserId { get; set; }
        [DynamoDBRangeKey]
        public string? Id { get; set; }
        public int Price { get; set; }
        public int Rating { get; set; }
        public string? Name { get; set; }
        public string? City { get; set; }
        public string? FileName { get; set; }
    }
}
