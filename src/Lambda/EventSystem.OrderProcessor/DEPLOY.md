# Despliegue de EventSystem.OrderProcessor en AWS Lambda

## Pre-requisitos

```bash
# 1. Instalar AWS CLI
# https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html

# 2. Configurar credenciales AWS
aws configure
# → AWS Access Key ID     : [tu key]
# → AWS Secret Access Key : [tu secret]
# → Default region name   : us-east-1
# → Default output format : json

# 3. Instalar el tooling de Lambda para .NET (ya lo hiciste)
dotnet tool install -g Amazon.Lambda.Tools
```

## Publicar la función en AWS

```bash
# Pararse en la carpeta del proyecto Lambda
cd src/Lambda/EventSystem.OrderProcessor

# Desplegar (empaqueta, sube a S3 y crea/actualiza la función Lambda)
dotnet lambda deploy-function

# Si es la primera vez, pedirá:
#   - Nombre de la función  : eventsystem-order-processor
#   - Rol IAM               : pegar el ARN de un rol con AWSLambdaBasicExecutionRole
```

## Crear el rol IAM (solo la primera vez)

```bash
# Crear rol de ejecución básico para Lambda
aws iam create-role \
  --role-name eventsystem-lambda-role \
  --assume-role-policy-document '{
    "Version":"2012-10-17",
    "Statement":[{
      "Effect":"Allow",
      "Principal":{"Service":"lambda.amazonaws.com"},
      "Action":"sts:AssumeRole"
    }]
  }'

# Adjuntar política de logs básica
aws iam attach-role-policy \
  --role-name eventsystem-lambda-role \
  --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole

# Copiar el ARN del rol → usarlo en el deploy-function
```

## Verificar que funciona

```bash
# Invocar la Lambda directamente desde CLI con un payload de prueba
dotnet lambda invoke-function eventsystem-order-processor \
  --payload '{
    "OrderId":      "550e8400-e29b-41d4-a716-446655440000",
    "CustomerName": "Jorge Correa",
    "Product":      "Laptop",
    "Total":        1200.00,
    "CreatedAt":    "2026-04-22T00:00:00Z"
  }'

# Respuesta esperada (descuento 15% por Total >= 1000):
# {
#   "OrderId": "550e8400...",
#   "DiscountPct": 15,
#   "DiscountAmount": 180.00,
#   "FinalTotal": 1020.00,
#   "Receipt": "====...",
#   ...
# }
```

## Configurar el NotificationService con las credenciales

Opción A — Variables de entorno (recomendado para desarrollo local):
```bash
set AWS_ACCESS_KEY_ID=tu_access_key
set AWS_SECRET_ACCESS_KEY=tu_secret_key
set AWS_DEFAULT_REGION=us-east-1
```

Opción B — Perfil en ~/.aws/credentials (ya configurado con `aws configure`)

El `LambdaInvokerService` usa `FallbackCredentialsFactory` que prueba
automáticamente variables de entorno → perfil local → IAM Role.

## Flujo completo verificado

```
POST http://localhost:5001/api/orders
  → RabbitMQ: orders.created.queue
    → NotificationService (BackgroundService)
      → Redis: notifications:{id}
      → Lambda: eventsystem-order-processor
          → calcula descuento
          → genera recibo
          → respuesta visible en logs del NotificationService
```
