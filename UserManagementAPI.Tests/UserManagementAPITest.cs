using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UserManagementAPI.Tests
{
    public class ExtendedUserManagementAPITests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public ExtendedUserManagementAPITests(WebApplicationFactory<Program> factory)
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
        public async Task HealthEndpoint_ReturnsOk()
        {
            // Act: Call the health check endpoint.
            var response = await _client.GetAsync("/health");

            // Assert: Expect a 200 OK status when the API (and its dependencies) are healthy.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task RequestCorrelationId_IsPresent_InResponse()
        {
            // Arrange: Ensure valid authentication.
            AddValidAuthHeader();

            // Act: Make a GET request to any endpoint (e.g., /users).
            var response = await _client.GetAsync("/users");

            // Assert: Verify that the 'X-Correlation-ID' header is present in the response.
            Assert.True(response.Headers.Contains("X-Correlation-ID"));
        }

        [Fact]
        public async Task GetUsers_ReturnsUnauthorized_WithoutToken()
        {
            // Ensure no Authorization header is present.
            _client.DefaultRequestHeaders.Authorization = null;

            // Act: Attempt to retrieve users.
            var response = await _client.GetAsync("/users");

            // Assert: Expect 401 Unauthorized.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetUsers_ReturnsOk_WithValidToken()
        {
            // Arrange: Add valid token.
            AddValidAuthHeader();

            // Act: Retrieve users.
            var response = await _client.GetAsync("/users");

            // Assert: Expect 200 OK.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task CreateUser_ReturnsCreatedAndThenGetUser()
        {
            // Arrange: Add valid token.
            AddValidAuthHeader();
            var newUser = new
            {
                name = "Test User",
                email = "test.user@example.com"
            };

            var content = new StringContent(JsonSerializer.Serialize(newUser), Encoding.UTF8, "application/json");

            // Act: Create a new user via POST.
            var createResponse = await _client.PostAsync("/users", content);

            // Assert: Expect 201 Created.
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            // Deserialize the created user.
            var createdResponseContent = await createResponse.Content.ReadAsStringAsync();
            var createdUser = JsonSerializer.Deserialize<User>(
                createdResponseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(createdUser);
            Assert.True(createdUser!.Id > 0);

            // Act: Retrieve the user using GET /users/{id}.
            var getResponse = await _client.GetAsync($"/users/{createdUser.Id}");

            // Assert: Expect 200 OK and the user data to match.
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

    // Minimal User model definition for testing (if not referenced from your main API project).
    public record User
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public string? Email { get; set; }
    }
}