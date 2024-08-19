﻿# DH.NRemoting - 协议通信库

协议通信库，包含RPC架构和HTTP架构，提供客户端服务端通信的基础框架，内置SRMP协议实现，并支持实现各种自定义协议。  



参考目录：

- [Remoting接口文档](doc/RemotingAPI.md)
- [SRMP简易远程消息协议](doc/srmp.md)



## RPC架构
客户端：**ApiClient**

服务端：**ApiServer**

应用客户端：**ClientBase**

特点：

1. 服务端根据Action把请求转发给各个Controller，用法跟WebApi一致。
2. 简单高性能，通信报文使用二进制序列化，中间没有任何损耗
3. 接口出入参支持灵活的Json序列化（常规接口），同时也支持高效的二进制序列化（图片视频）
4. TCP/UDP长会话，支持服务端主动下发消息
5. 典型连接数，单机1万TCP长连接，最高400万
6. 典型吞吐数，单机10万TPS，最高2266万TPS
7. 服务端可寄宿于控制台、Web项目、桌面应用、IoT嵌入式应用
8. 支持集群部署横向扩展，每个客户端跟其中一个服务端维持长连接，所有请求落到该服务器

代表性应用（**蚂蚁调度AntJob**）：

1. 客户端AntClient继承自ClientBase，通过Tcp/Udp等协议连接服务端ApiServer，进行登录、心跳等操作。
2. 客户端使用应用AppId和AppSecret登录，获得令牌，后续无需携带或验证令牌，直到令牌过期重新登录。




## HTTP架构
客户端：**ApiHttpClient**

服务端：**ASP.NET WebApi**

应用客户端：**ClientBase**

应用服务端：**BaseDeviceController**

特点：

1. 标准ASP.NET WebApi作为服务端，重用现有技术栈，包括接口测试和集群部署管理。
2. 客户端ApiHttpClient是标准HttpClient的进一步封装，支持多服务端地址负载均衡。
3. 默认通信使用Json序列化，不适合传输文件、图片和视频等二进制数据。
4. 服务端提供BaseController基类，封装了令牌验证等鉴权机制
5. 服务端提供BaseDeviceController基类，封装常见的登录、心跳和更新等接口
6. 通过WebSocket长连接实现指令下发，心跳保活
7. 典型连接数，单机1000并发连接
8. 典型吞吐数，单机1万TPS
9. 服务端仅寄宿于 Kestrel和IIS
10. 支持集群部署横向扩展，客户端每次请求都可能分流到不同应用服务器

代表性应用（**轻量级IoT平台ZeroIoT**）：

1. 客户端HttpDevice继承自ClientBase，通过Http/Https协议连接服务端WebApi，进行登录、注销、心跳和更新等操作。
2. 客户端使用设备DeviceCode和DeviceSecret登录，获得令牌，后续每次请求头都需要带上令牌。
3. 在心跳时检测并维持WebSocket长连接。