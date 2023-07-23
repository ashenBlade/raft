# Описание протокола

Для взаимодействия между клиентом и сервером используется клиент-серверный протокол, основанный на TCP.
Клиент и сервер общаются между собой пакетами определенного формата.

Байты передаются в формате Big-Endian

# Пакеты 

Каждый пакет имеет собственную структуру. 
Каждый пакет первым байтом имеет маркерныйбайт, указывающий на его тип.
Для удобства, маркеры представляются символами ASCII.
Далее, содержимое определяется типом пакета

## Data Request

Пакет, передающий основной запрос к узлу. 
Тело представляет собой сериализованное тело операции.
После маркера идет 4 байтное целое число, определяющее длину тела 

Формат пакета:

| Маркер | Длина | Тело   |
|--------|-------|--------|
|  'D'   | Int32 | Byte[] |

## Data Response

Пакет, передающий ответ на запрос операции.
После маркера указывается 4 байтная длина тела запроса.
Содержимое тела зависит от переданного прежде **Data Request**

Формат пакета:

| Маркер | Длина | Тело   |
|--------|-------|--------|
| 'R'    | Int32 | Byte[] |

## ErrorResponse

Пакет посылаемый клиенту сервером, если возникла ошибка при выполнении.
Описание ошибки будет представлено в поле 'Message'

Сообщение представлено в поле 'Message'. 
Это закодированная UTF-8 строка. 
Ее длина указана в поле 'Length' 

Формат:

| Marker | Length | Message |
|--------|--------|---------|
| 'e'    | Int32  | Byte[]  |

## NotALeader

Пакет отправляется сервером, если на узел не являющийся лидером в кластере пришла модифицирующая команда.

В поле 'LeaderId' указывается ID узла лидера текущего кластера.
При получении этого пакета, необходимо переадресовать присланный прежде пакет узлу с указанным Id.

Формат:

| Marker | LeaderId |
|--------|----------|
| 'l'    | Int32    |
