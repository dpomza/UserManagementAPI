using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using UserManagementAPI;

namespace UserManagementAPI.Tests
{
    public class UserManagementAPITests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public UserManagementAPITests(WebApplicationFactory<Program> factory)
        {
            // Create an HttpClient to interact with the in-memory test server.
            _client = factory.CreateClient();
        }

        // Helper: Adds a valid Authorization header.
        private void AddValidAuthHeader()
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "mysecrettoken");
        }

        [Fact]
        public async Task GetUsers_ReturnsUnauthorized_WithoutToken()
        {
            // Ensure no Authorization header is present.
            _client.DefaultRequestHeaders.Authorization = null;

            // Act
            var response = await _client.GetAsync("/users");

            // Assert: Expect 401 Unauthorized.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetUsers_ReturnsOk_WithValidToken()
        {
            // Arrange: Add valid token.
            AddValidAuthHeader();

            // Act
            var response = await _client.GetAsync("/users");

            // Assert: Expect 200 OK.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task CreateUser_ReturnsCreatedAndThenGetUser()
        {
            // Arrange:
            AddValidAuthHeader();
            var newUser = new
            {
                name = "Test User",
                email = "test.user@example.com"
            };

            var content = new StringContent(JsonSerializer.Serialize(newUser), Encoding.UTF8, "application/json");

            // Act: Create a new user (POST).
            var createResponse = await _client.PostAsync("/users", content);

            // Assert: Expect 201 Created.
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            // Deserialize the created user.
            var createdResponseContent = await createResponse.Content.ReadAsStringAsync();
            var createdUser = JsonSerializer.Deserialize<User>(
                createdResponseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(createdUser);
            Assert.True(createdUser!.Id > 0);  // Confirm a valid ID was assigned.

            // Act: Retrieve the user using GET /users/{id}.
            var getResponse = await _client.GetAsync($"/users/{createdUser.Id}");

            // Assert: Expect 200 OK.
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var getResponseContent = await getResponse.Content.ReadAsStringAsync();
            var retrievedUser = JsonSerializer.Deserialize<User>(
                getResponseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(retrievedUser);
            Assert.Equal(createdUser.Id, retrievedUser!.Id);
            Assert.Equal(createdUser.Name, retrievedUser.Name);
            Assert.Equal(createdUser.Email, retrievedUser.Email);
        }
    }

    // Minimal version of the User record (must match your API model).
    public record User
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public string? Email { get; set; }
    }
}