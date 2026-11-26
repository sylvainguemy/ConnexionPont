# Architecture et périmètre V0.2

| Confirmé | Hypothèse à vérifier | Bloquant pour la recette réelle |
| --- | --- | --- |
| PC Windows chez le client ; accès RDP au PC puis rebond manuel | Windows Pro / Entreprise / Éducation et droits RDP disponibles | IP publique réelle, redirection Freebox et compte Windows RDP |
| SSH direct depuis le terminal de Sylvain vers un serveur interne, port 22 | Le PC client résout et joint ce serveur | Adresse du serveur SSH et compte / clé SSH |
| IP publique fixe chez Free ; relais hébergé sur le PC de Sylvain | Port externe disponible et trafic TLS direct autorisé | Accès au réseau réel pour qualifier la liaison |
| Publication dans sylvainguemy/ConnexionPont | .NET Framework 4.8 présent | Aucun pour compiler les EXE |

## Choix

C# / .NET Framework 4.8, WinForms et bibliothèques Windows uniquement : deux EXE légers, sans téléchargement de dépendances pendant l'installation. Le transport utilise les primitives TLS standard `SslStream` / Schannel, sans protocole de chiffrement réseau inventé. La V0.1 n'intègre pas Chisel.

Le poste Accès (EXE Relais) écoute sur toutes les interfaces IPv4 sur TCP 8443 et sur trois ports de données uniquement en boucle locale : RDP 13389, SSH 10022 et proxy web SOCKS5 1080. La Freebox redirige TCP 443 vers le port d'écoute du poste Accès. Le pont (EXE Agent) initie toutes les connexions vers le poste Accès.

Les ports 8080 et 8090 du serveur web configuré sont les seules destinations acceptées par SOCKS5. Chaque demande CONNECT validée devient une commande WEB8080 ou WEB8090 ; le pont choisit lui-même la destination fixe du profil. Le navigateur est ouvert dans un profil séparé avec un proxy SOCKS5 explicite, sans réglage proxy global. Cela conserve les URL, les en-têtes Host, les cookies et le transport TCP des applications, y compris les connexions WebSocket.

## Contrat réseau RDT2

Chaque connexion est authentifiée mutuellement par TLS 1.2 avant la lecture de commandes ASCII. Les empreintes SHA-256 des deux certificats sont fixées lors de l'appairage, sans dépendance à une autorité racine publique. Leur validité temporelle est contrôlée.

| Canal / commande | Sens | Effet |
| --- | --- | --- |
| `RDT2 CONTROL` puis `READY` | Agent vers relais, puis réponse | Enregistre l'unique agent connecté |
| `PING` / `PONG` | Agent / relais | Maintient la connexion ; délai de lecture 45 s |
| `OPEN <id> RDP / SSH / WEB8080 / WEB8090` | Relais vers agent | Demande une connexion à l'une des destinations autorisées |
| `RDT2 DATA <id>` puis `GO` | Agent vers relais, puis réponse | Associe le flux à une demande en cours, puis transmet les octets bruts |

Les identifiants de session sont des GUID de 128 bits ; seul un agent possédant le certificat appairé peut fournir un canal DATA. Les connexions inconnues, les sessions rejouées et les commandes trop longues sont fermées. Aucun hôte ni port arbitraire n'est accepté via le protocole ; SSH est fixé par le profil, RDP à `127.0.0.1:3389`, WEB8080 et WEB8090 au serveur web du profil. Les deux EXE doivent être mis à jour ensemble. Les profils V0.1 restent lisibles ; leurs nouveaux champs web ont des valeurs par défaut sans recréation de clés.

Délais : connexion TCP 10 s ; négociation TLS / première commande 10 s ; contrôle 45 s ; ouverture d'une destination 20 s ; requête SOCKS locale 5 s. 32 sessions de données, 24 négociations entrantes simultanées maximum. La fermeture du canal de contrôle ferme les sessions associées. Après arrêt de la connexion, les sockets suivis sont fermés.

## Configuration et fichiers

`Settings.cs` : modèle versionné, validation et chiffrement local DPAPI. `Security` : génération de certificats, empreintes et format de profil portable. `Transport.cs` : négociation TLS, framing borné et copie bidirectionnelle. `Relay.cs` / `Agent.cs` : orchestration des canaux. `WindowsUi.cs` : interfaces et diagnostics. Aucun secret n'est commité ; les clés sont créées au premier enregistrement.

Le profil portable est composé de l'en-tête RDPTUN01, d'un sel de 32 octets, d'un IV de 16 octets, du contenu AES-256-CBC et d'un tag HMAC-SHA256 de 32 octets. Deux clés distinctes sont dérivées par PBKDF2-SHA256 (200 000 itérations). L'authentification du fichier est vérifiée avant déchiffrement.

Le profil est un titre d'accès : quiconque possède le fichier et son mot de passe peut utiliser l'identité agent correspondante. Après import, DPAPI protège les clés pour le compte Windows courant. Un poste ou un compte compromis reste hors du modèle de protection. Le profil n'exporte jamais le certificat privé du relais.

## Critères de recette

1. Authentification mutuelle valide ; refus d'une autre identité ou d'une empreinte incorrecte.
2. Transfert binaire bidirectionnel intact et destinations RDP / SSH distinctes.
3. Utilisation simultanée et libération des sessions à la fermeture.
4. Reconnexion après redémarrage du relais ; refus d'accès sans agent.
5. Profil portable chiffré, mauvais mot de passe et modification refusés ; persistance DPAPI.
6. Sur site : session `mstsc` réelle et session `ssh` réelle depuis un réseau externe à la Freebox.

Les seuils de débit et de latence WAN ne sont pas spécifiés : ils restent à mesurer selon les deux connexions Internet. Les tests utilisent des payloads locaux ; ils ne constituent pas une mesure WAN. La disponibilité dépend des deux PC et des sessions Windows ouvertes.
