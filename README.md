# MQTT Relay Control

Small .NET console application / daemon for controlling a USB-serial relay from an MQTT topic.
Run the app as a background task which subscribes to an MQTT topic specified in the config file.
When the topic is updated with "open" or "close" values the relay responds accordingly.

## Hardware Requirements

- Designed for [USB Relay Module Control Switch - LCUS-1 Type DC 5V with High Performance Control Chip](https://www.amazon.com/dp/B09VTK98S7?psc=1&ref=ppx_yo2ov_dt_b_product_details)


## Example Configuration

```yaml
serial-port:
  port: /dev/tty.usbserial-10  # path to serial port on Linux/MacOS, or COM port on Windows
  baud: 9600                   # Baud rate for device, in this case 9600.
mqtt:
  host: mqtt                   # MQTT server host name or IP address
  port: 1883                   # MQTT port
  topic: boatpi/relay/ais-send # Topic to listen for changes
logging:
  # filename: logfile.txt      # Optional log to file instead of Standard Out.
  level: Debug                 # Log level, Error, Warn, Info, or Debug.    
```

## Example Runtime

Run the program using the .NET CLI interface. The `-c` parameter indicates
to read the configuration from a file. If you run the program with a configuration
file or without an MQTT server defined, the program launches in an interactive mode
listing the available serial connections and allowing the relay to be controlled manually.

```bash
dotnet run -- -c config.yml
```


## Docker Container

You can also run this as a docker container:

```bash
docker run --env RELAY_CONFIG_PATH=/App/config.yml --volume ./config.yml:/App/config.yml:ro --volume /dev/tty.usbserial-10:/dev/tty.usbserial-10 --detach mqtt-relay-control 
```

You can also use Docker Compose:

```yaml
version: '3'
services:
  mqtt-relay-control:
    container_name: mqtt-relay-control
    image: mqtt-relay-control
    env:
      - RELAY_CONFIG_PATH=/App/config.yml
    volumes:
      - ./config.yml:/App/config.yml:ro
      - /dev/tty.usbserial-10:/dev/tty.usbserial-10
    restart: unless-stopped
```