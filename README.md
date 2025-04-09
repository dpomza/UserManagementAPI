# User Management API
### This project uses Redis. To be able to test this here is the list of things to do:

## Install Redis:

### 1. Installing Redis Locally
  ### a. Windows: 
   Although Redis is originally built for Linux, you have several options on Windows:
   
  * Use WSL: If you have Windows Subsystem for Linux (WSL) installed, you can follow the standard Linux instructions. For example:
    
   ```sh
   sudo apt update
   sudo apt install redis-server
   sudo systemctl enable redis-server.service
   sudo systemctl start redis-server.service
   ```
      
   * Official Windows Port: You can try the Memurai (a Redis-compatible solution for Windows) or find community-supported builds.
   * Docker: if you have Docker installed the easiest approach is to run a Redis container (see 2)
    
  ### b. macOS / Linux: 
   Redis is available via package managers:
   ### On macOS (with Homebrew)
      
   ```sh
   brew install redis
   brew services start redis
   ```

   ### On Ubuntu/Linux:
   ```sh
   sudo apt update
   sudo apt install redis-server
   sudo systemctl enable redis-server.service  
   sudo systemctl start redis-server.service
   ```

### 2. Using Docker
   If you have Docker installed, you can quickly run Redis with the following command:
   ```sh
   docker run --name my-redis -d -p 6379:6379 redis
   ```
   This command pulls the latest Redis image, runs it in a container named , and maps port 6379 from the container to your local machine.


# Testing

### Using the UserManagementAPITest project

* From Visual Studio: You can use the Test Explorer (available from Test menu) or directly select Run All Tests
* From Visual Studio Code (and Visual Studio):
```sh
dotnet test
```

### Using the requests.http file
