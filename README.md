# BoatPi Signal-K / MQTT Device Controller

A small daemon service designed for managing devices in a marine environment. This service
was designed to work with automation platforms like [Home Assistant](https://www.home-assistant.io/),
[Zigbee2MQTT](https://www.zigbee2mqtt.io/), and [Signal-K](https://signalk.org/).

The app serves two primary purposes:

1. Enabling remote control of serial relay controllers from Home Assistant or via [MQTT](https://mqtt.org/).
2. Mapping data from Zigbee2MQTT into Signal-K so the data can be available to other devices.

## Relay Control

I started this project because I had a few devices onboard that I wanted to be able to remote
control. I used a USB relay module, like the
[LCUS-1 Type DC 5V](https://www.amazon.com/dp/B09VTK98S7?psc=1&ref=ppx_yo2ov_dt_b_product_details).
Which is connected to a Raspberry Pi where the service is running.


## Mapping between Zigbee2MQTT and Signal-K

A second use for this project is mapping between MQTT topics published by Zigbee2MQTT and
Signal-K to make data available for other onboard devices. In this case I have a series of
Zigbee temperature monitors that I wanted to make available on my boat network. The service
seamlessly listens for updates from [Zigbee2MQTT](http://www.zigbee2mqtt.io/) and republishes the data in the correct
format for Signal-K to pick up via [signalk-mqtt-bridge](https://www.npmjs.com/package/signalk-mqtt-bridge).

## Prereqs

You need to have an MQTT server installed and running. [Eclipse Mosquitto](https://www.mosquitto.org/) is a light-weight
and open source MQTT server that's easy to setup and run on a Raspberry Pi.


## Configuration

Configuring the service happens through a YAML configuration
file to specify the connection to the MQTT server and what
actions to perform.

### MQTT Configuration

```yaml
mqtt:
  host: mqtt.lan               # MQTT server host name or IP address
  port: 1883                   # MQTT port
  keep_alive_seconds: 30       # How frequently the client pings the server to ensure it stays connected (seconds).
  reconnect_timeout: 60        # If the client loses connection, how frequently it attempts to reconnect (seconds).
  connection_timeout: 10       # How long the client tries to connect before giving up (seconds).
  initial_connection_attempts: 10  # Number of connection attempts before the program exits.
```

### Logging Configuration

```yaml
logging:
  filename: logfile.txt       # Filename for log output, if omitted console logging is used.
  level: Debug                # Log level: Debug, Info, Warn, Error
```

### Relay Controller

You can define zero or more relay devices which can be controlled
via MQTT topics.

Each control device consists of a `name`, `serial_port`, `guid`, `entity_id`, and `icon`.
These properties are primarily used when publishing the controller to be discovery
in Home Assistant and to configure the topic used for the controller.

Each serial port is configured with the path to the port and a baud rate for
the serial communication with the device.

```yaml
relay_control:
  - name: "Transmit AIS position"               # Name of the device being controlled
    serial_port:      
      port: /dev/tty.usbserial-10               # Port or path for serial device
      baud: 9600                                # Baud rate
    guid: switch.boat_ais_transmission6ad32245  # Unique identifier
    entity_id: boat_ais_transmission            # Home assistant entity_id
    icon: "mdi:broadcast"                       # Home assistant icon
- name: "Navigation Lights"
    serial_port: 
      port: /dev/tty.usbserial-11
      baud: 9600
    guid: switch.navigation_lights9190a
    entity_id: navigation_lights
    icon: "mdi:light"
```

### Home Assistant

The service can be configured to publish discovery information about
the relay control devices to MQTT according to the auto discovery protocol
for Home Assistant.

```yaml
home_assistant:
  discovery: true                       # enable discovery
  discovery_prefix: "homeassistant"     # prefix for discovery topics
  device_unique_id: "33564eb9"          # Unique ID if you are using this service multiple times
  device_name: "BoatPi MQTT Helper"     # Device name for the service
  device_topic_prefix: "boatpi/devices" # Topic prefix for controllable devices
```

### Signal-K Mapping

The service can also map data from one topic to another to enable
data sharing between Zigbee2MQTT and Singal-K when using the 
signalk-mqtt-bridge plug-in.

```yaml
signalk:
  system_id: "368327460"      # Last segment of the UUID for your Signal-K server
  mqtt_mapping: 
    - source: "zigbee2mqtt"   # Source of the data, always zigbee2mqtt
      format: "temperature"   # Data format / probe type, always temperature
      source_topic: "zigbee2mqtt/CabinTemperature"   # Source topic for the data from Zigbee2MQTT
      dest_topic: "vessels/self/environment/inside/mainCabin"  # Destination path in Signal-K
```

For Zigbee temperature probes, the service will try to map values
for temperature, relative humidity, and pressure from the Zigbee system
into Signal-K. Any data points which cannot be retrieved are ignored.


## Run from Source

If you have the .NET CLI installed and the .NET 6.0 runtime available you can
use the CLI to run this service locally.

```bash
git clone https://github.com/rgregg/mqtt-relay-control.git

dotnet run -- config.yml
```

## Publish

You can also publish the source to a standalone executible and
register it to run with systemd.

First, publish the release configuration of the service for your desired
runtime environment (in this example, Linux-ARM64 for Raspberry Pi).

```bash
dotnet publish --configuration Release --runtime linux-arm64 --self-contained /p:PublishSingleFile=true
```

Then copy the published files from the output directory to a location
where you will run the service. Something like `/opt/mqtt-relay-control/`
works well. Make sure to copy your configuration file into the same folder. 

Finally, you can define the service in systemd by creating
a new service file, `mqtt-relay-control.service` in 
`/etc/systemd/system/`:

```ini
# Move this file to /etc/systemd/system/

[Unit]
Description=MQTT Relay Controller
After=network.target
StartLimitIntervalSec=0

[Service]
Type=exec
Restart=always
RestartSec=10
User=root
ExecStart=/opt/mqtt-relay-control/mqtt-relay-control config.yml

[Install]
WantedBy=multi-user.target
```

After you have setup the file, reload systemd and start the service.

```bash
systemctl daemon-reload
systemctl start mqtt-relay-control.service
systemctl status mqtt-relay-control.service
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