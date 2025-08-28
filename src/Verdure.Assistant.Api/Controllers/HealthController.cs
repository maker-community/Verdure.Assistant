using Microsoft.AspNetCore.Mvc;

namespace Verdure.Assistant.Api.Controllers
{
    /// <summary>
    /// 健康检查和测试控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly ILogger<HealthController> _logger;

        public HealthController(ILogger<HealthController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 健康检查
        /// </summary>
        [HttpGet]
        public IActionResult Health()
        {
            return Ok(new 
            { 
                Status = "Healthy", 
                Timestamp = DateTime.Now,
                Version = "1.0.0",
                Service = "Verdure Assistant API"
            });
        }

        /// <summary>
        /// 连接测试
        /// </summary>
        [HttpGet("test")]
        public IActionResult Test()
        {
            _logger.LogInformation("健康检查测试请求");
            return Ok(new 
            { 
                Message = "连接正常", 
                Timestamp = DateTime.Now,
                Success = true
            });
        }

        /// <summary>
        /// 获取API版本信息
        /// </summary>
        [HttpGet("version")]
        public IActionResult Version()
        {
            return Ok(new 
            { 
                Version = "1.0.0",
                BuildDate = DateTime.Now.ToString("yyyy-MM-dd"),
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            });
        }
    }
}
