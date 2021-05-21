job "dk-demo" {
  datacenters = ["dc1"]

  type = "service"

  reschedule {
    delay = "30s"
    delay_function = "constant"
    unlimited = false
  }

  update {
    max_parallel      =  1
    health_check      = "checks"
    min_healthy_time  = "10s"
    healthy_deadline  = "5m"
    progress_deadline = "10m"
    auto_revert       =  true
    stagger           = "30s"
  }


  group "dk-demo" {
    count = 1

    network {
      port "http" {
        to = 8080
      }
    }

    update {
      canary       =  0
      auto_promote = false
    }

    restart {
      interval = "101m"
      attempts = 2
      delay    = "15s"
      mode     = "fail"
    }

    task "dk-demo" {
      template {
          data = <<EOH
              USER_NAME="{{key "acr/username"}}"
              PASS_WORD="{{key "acr/password"}}"
              EOH
          destination = "secrets/file.env"
          env         = true
      }
      driver = "docker"
      config {
        image = "securanonprod.azurecr.io/dk-demo:V7"

        auth {
          username = USER_NAME
          password = PASS_WORD
        }

        force_pull = false
        ports = ["http"]

      }

      service {
        name = "dk-demoService"
        port = "http"
        
        check {
          name     = "dk-demo health using http endpoint '/health'"
          port     = "http"
          type     = "http"
          path     = "/health"
          method   = "GET"
          interval = "10s"
          timeout  = "2s"
        }

        tags = [
          "type:webApp",
          "owners:dk",
          "version:v4",
          "language:dotnet",
          "env:Dev",
          "releaseType:Canary",
          "partOf:Nothing",
          "urlprefix-/DKWebApp"
        ]
        
        check_restart {
          limit = 3
          grace = "10s"
          ignore_warnings = false
        }
      }

      resources {
        cpu    = 125 # MHz
        memory = 500 # MB
      }
    }
  }
}
