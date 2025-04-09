namespace UserManagementAPI.Models
{
    public record User
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public string? Email { get; set; }
    }
}