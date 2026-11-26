# Installation et utilisation — ConnexionPont V0.2

## 1. Identifier les deux PC

| Machine | Rôle dans ce guide | Programme |
| --- | --- | --- |
| Votre PC personnel, qui accède aux serveurs | Accès chez vous | ConnexionPont.Relais.exe |
| PC Windows chez le client, déjà connecté à son réseau interne | Pont chez le client | ConnexionPont.Agent.exe |

Le mot technique « Relais » de la V0.1 désigne le programme lancé **sur votre PC**, qui reçoit le tunnel. Le PC de pont exécute l'agent. Les noms des EXE et dossiers de configuration sont conservés pour les mises à jour.

Prérequis : .NET Framework 4.8 déjà présent sur les deux PC ; Windows compatible et autorisation d'exécuter les EXE. Pour recevoir du RDP, le pont doit être sous Windows Pro, Entreprise ou Éducation, avec RDP déjà activé et votre compte autorisé. SSH et web fonctionnent indépendamment de RDP.

## 2. Installer ou lancer le poste Accès chez vous

Extraire complètement `ConnexionPont-Windows-v0.2.0.zip`. Conserver chaque `.exe.config` à côté de son EXE.

Option portable : lancer directement `ConnexionPont.Relais.exe` depuis le dossier extrait.

Option installation utilisateur, facultative :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Installer.ps1 -Role Acces
```

`-Role Relais` reste accepté pour compatibilité. Le script crée les raccourcis et copie le programme dans `%LOCALAPPDATA%\ConnexionPont\Applications\Relais`. La copie ne demande pas de droits administrateur. Pour démarrer la fenêtre à l'ouverture de session, ajouter `-DemarrerALOuvertureSession` ; le bouton de connexion reste à actionner.

## 3. Configurer votre poste Accès

Dans la fenêtre **Accès chez vous** :

1. Saisir votre IP publique fixe ou un nom DNS, sans `https://` ni port.
2. Conserver le port Internet **443**, le port d'écoute **8443**, le RDP local **13389** et le SSH local **10022**, sauf conflit.
3. Renseigner le serveur SSH interne, tel qu'il est accessible depuis le pont. Son port reste **22**.
4. Renseigner **Serveur web interne** avec l'IP du serveur qui héberge vos applications 8080 et 8090. Conserver **Proxy web local 1080**. Un champ serveur vide désactive l'accès correspondant.
5. Cliquer **Enregistrer**. Les certificats sont créés au premier enregistrement et conservés ensuite.
6. Cliquer **Exporter l'agent**, choisir un mot de passe d'au moins 12 caractères puis créer `client.rdptunnel`.
7. Transmettre au pont l'EXE agent, son `.config` et le profil ; communiquer le mot de passe du profil séparément.

La configuration de votre poste est protégée pour votre compte Windows dans `%LOCALAPPDATA%\ConnexionPont\Relais\settings.bin`. Ne pas transmettre ce fichier au pont. Le profil exporté ne contient pas la clé privée du poste Accès.

## 4. Freebox et pare-feu sur votre PC

Attribuer une IP LAN stable à votre PC dans la Freebox, de préférence avec un bail DHCP statique.

Dans Freebox OS, paramètres avancés / Gestion des ports :

| Paramètre | Valeur |
| --- | --- |
| IP de destination | IP LAN de votre PC Accès |
| Protocole | TCP |
| Port externe début / fin | 443 / 443 |
| Port interne de destination | 8443 |

Vérifier que le port externe est disponible et compris dans votre plage IPv4. L'option Free **IPv4 fixe full-stack** donne accès à tous les ports et peut changer votre IP : relever la nouvelle IP et mettre à jour / réexporter le profil si nécessaire.

Sur **votre PC**, ouvrir PowerShell **en administrateur**. Si vous avez utilisé Installer.ps1 :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Configurer-PareFeu-Relais.ps1 -Port 8443
```

Si le programme est lancé directement depuis le dossier extrait, exécuter cette commande depuis ce dossier :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Configurer-PareFeu-Relais.ps1 -Port 8443 -Programme "$PWD\ConnexionPont.Relais.exe"
```

L'option `-ExecutionPolicy Bypass` vaut uniquement pour ce processus PowerShell. Elle ne modifie pas la politique permanente du poste. Une politique imposée par l'organisation reste prioritaire : contacter l'administrateur si elle bloque la commande.

La règle autorise uniquement le port d'écoute TLS du programme. Aucun port RDP, SSH ou proxy local n'est publié sur la Freebox. Pour le web, **aucune redirection supplémentaire pour 8080, 8090 ou 1080** n'est nécessaire.

Cliquer **Démarrer l'accès**. Votre PC doit rester allumé et éveillé.

## 5. PC de pont chez le client : sans installation ni droits administrateur

Cette procédure ne lance aucun installateur ni script PowerShell sur le pont.

1. Sur le PC de pont, créer un dossier dans votre profil utilisateur, par exemple `Documents\ConnexionPont`.
2. Y copier ces trois fichiers :

| Fichier | Utilité |
| --- | --- |
| ConnexionPont.Agent.exe | Programme du pont |
| ConnexionPont.Agent.exe.config | Configuration du runtime Windows |
| client.rdptunnel | Profil chiffré exporté depuis votre PC Accès |

3. Double-cliquer **ConnexionPont.Agent.exe**. La fenêtre porte le titre **Pont chez le client**.
4. Cliquer **Importer le profil**, sélectionner `client.rdptunnel` et saisir son mot de passe.
5. Cliquer **Diagnostic** pour vérifier les destinations autorisées. Un échec RDP ne bloque pas les fonctions SSH ou web.
6. Cliquer **Connecter le pont**. Les fenêtres des deux PC doivent indiquer **CONNECTÉ**.
7. Garder la fenêtre ouverte, le PC allumé et la session Windows ouverte. Verrouiller le poste conserve les processus ; fermer la session les arrête.

Le programme enregistre son profil uniquement dans `%LOCALAPPDATA%\ConnexionPont\Agent\settings.bin`, chiffré pour votre compte Windows. Il n'installe pas de service, ne modifie pas le pare-feu ou le proxy système du pont et n'ajoute pas de certificat racine de confiance.

.NET Framework 4.8 doit déjà être installé. Si ce prérequis manque, son installation relève de l'administrateur du poste. Le réseau du client doit autoriser la connexion sortante TLS du pont vers votre IP publique, port 443. Le transport n'est pas du HTTPS et ne prend pas en charge un proxy HTTP CONNECT obligatoire.

Pour RDP, le service doit déjà être activé et le compte autorisé ; son activation nécessite un administrateur. **SSH et web ne nécessitent pas d'activer RDP**. Les destinations internes doivent être joignables depuis le pont : serveur SSH sur 22, serveur web sur 8080 / 8090.

Pour retirer la version portable : fermer la fenêtre puis supprimer les fichiers copiés. Le profil enregistré se trouve dans le chemin ci-dessus ; le retirer si vous souhaitez supprimer l'appairage de ce compte utilisateur.

Option installation utilisateur facultative si autorisée, sans élévation : `Installer.ps1 -Role Pont` (`-Role Agent` reste accepté). Cette option n'est pas nécessaire pour le mode portable.

## 6. Utiliser RDP et SSH depuis votre PC

```powershell
# Bureau du PC de pont
mstsc /v:127.0.0.1:13389

# SSH vers le serveur interne via le pont (le serveur écoute sur 22)
ssh -p 10022 utilisateur@127.0.0.1

# Clé SSH conservée sur votre PC
ssh -i C:\Users\vous\.ssh\id_ed25519 -p 10022 utilisateur@127.0.0.1
```

Les boutons **Ouvrir RDP**, **Ouvrir SSH** et **Copier la commande SSH** sont disponibles après connexion du pont. La commande copiée inclut un `HostKeyAlias` par profil pour distinguer les clés des serveurs. Vérifier l'empreinte SSH lors de la première connexion.

Les identifiants RDP sont ceux du pont. Les identifiants / clés SSH sont ceux du serveur interne. Aucun mot de passe RDP/SSH n'est enregistré par ConnexionPont. RDP sur un poste Windows peut verrouiller ou déconnecter l'affichage local.

## 7. Navigateur vers les applications 8080 et 8090

1. Vérifier que **Serveur web interne** est renseigné sur votre poste Accès.
2. Après enregistrement, exporter et réimporter le profil sur le pont ; connecter les deux côtés.
3. Cliquer **Web 8080** ou **Web 8090** dans la fenêtre Accès chez vous.
4. Une fenêtre Edge ou Chrome dédiée au tunnel s'ouvre. L'adresse est celle du serveur interne, avec son port d'origine. Vous pouvez saisir directement les deux URL dans **cette fenêtre**.

Les connexions sont transportées vers le pont, qui contacte le serveur interne sur 8080 ou 8090. Les URL, les chemins, les cookies et les en-têtes Host ne sont pas réécrits. Les deux applications conservent leurs propres origines et ports. Le proxy transporte les flux TCP, y compris les échanges HTTP et les connexions mises à niveau telles que WebSocket.

Le proxy local est `127.0.0.1:1080` par défaut. Le port 1080 n'est à ouvrir ni sur la Freebox, ni sur le serveur interne. Les seules destinations web autorisées sont le serveur du profil sur 8080 et 8090. Les liens vers d'autres serveurs / ports sont refusés par cette fenêtre ; utiliser votre navigateur habituel pour les autres sites.

Le profil navigateur est distinct de votre profil habituel, dans `%LOCALAPPDATA%\ConnexionPont\Navigateur`. Les cookies peuvent y être conservés. Edge ou Chrome doit être présent sur **votre PC**, pas sur le pont. Une politique navigateur imposée peut empêcher l'application des paramètres de proxy ; vérifier les politiques si le navigateur n'utilise pas le tunnel.

Cliquer sur les mêmes liens dans une fenêtre de navigateur ordinaire ne la configure pas automatiquement pour le tunnel. Ouvrir d'abord la fenêtre dédiée avec un des boutons web.

## 8. Mise à jour depuis V0.1

1. Prévoir un moyen de relancer le programme sur le pont : l'arrêt de l'agent interrompt une éventuelle session RDP utilisant ce tunnel.
2. Fermer les applications des deux PC, remplacer **les deux EXE et leurs .config** par ceux de la V0.2. Les dossiers et clés existants sont conservés.
3. Sur votre PC, relancer Accès, renseigner le serveur web et le port proxy local, puis enregistrer.
4. Exporter un nouveau profil et l'importer sur le pont, puis connecter.

Les certificats sont conservés. Les anciens profils restent lisibles mais ne configurent pas le web. La V0.2 utilise RDT2 : les deux exécutables doivent être mis à jour ensemble.

## 9. Diagnostic

| Symptôme | Vérification |
| --- | --- |
| Script bloqué par PowerShell | Utiliser la commande complète avec `powershell.exe -NoProfile -ExecutionPolicy Bypass -File` ; respecter les éventuelles politiques d'organisation |
| Règle de pare-feu refusée | Ouvrir PowerShell en administrateur sur votre PC Accès et donner le chemin du véritable EXE utilisé |
| Pont en attente | IP publique, Freebox, port d'écoute, règle Windows, PC Accès éveillé, version V0.2 sur les deux côtés |
| Certificat refusé | Date/heure, profil correspondant et certificats non expirés ; conserver leur validation |
| RDP échoue | Édition Windows, RDP déjà activé, compte autorisé, port local 3389 sur le pont |
| SSH échoue | Adresse interne et port 22 joignables depuis le pont, identifiants / clé SSH |
| Boutons web grisés | Serveur web vide, profil non réexporté / réimporté, ou pont non connecté |
| Web inaccessible dans le navigateur habituel | Ouvrir la fenêtre dédiée avec Web 8080 / Web 8090 |
| Web inaccessible dans la fenêtre dédiée | Diagnostic des ports 8080 / 8090 depuis le pont ; adresse du serveur dans le profil ; politique proxy Edge / Chrome |
| Adresse déjà utilisée / port 1080 occupé | Choisir un autre port Proxy web local sur votre PC, enregistrer et redémarrer l'accès ; rouvrir le navigateur via les boutons |

Le diagnostic de l'agent vérifie les ports TCP, pas les identifiants applicatifs. Tester ensuite une session RDP réelle, `hostname` en SSH et les deux applications web depuis un réseau réellement externe à la Freebox.

## 10. Arrêt, reprise, désinstallation

Fermer une fenêtre ou cliquer **Arrêter** coupe les sessions. Après une coupure réseau, le pont réessaie automatiquement jusqu'à 30 secondes entre tentatives. Les connexions RDP/SSH/web interrompues doivent être rouvertes.

Pour la version installée, fermer les EXE puis lancer, depuis le dossier contenant le script :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Desinstaller.ps1 -Role Acces
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Desinstaller.ps1 -Role Pont

# Option explicite pour retirer aussi la configuration de ce compte Windows
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Desinstaller.ps1 -Role Pont -EffacerConfiguration
```

Pour retirer la règle entrante Windows, depuis un PowerShell administrateur :

```powershell
Remove-NetFirewallRule -Name ConnexionPont-Relais-TLS
```

Retirer aussi la redirection Freebox si elle n'est plus utilisée. Les données du navigateur dédié sont conservées ; supprimer son dossier si vous souhaitez retirer ses cookies. Les certificats expirent au bout de deux ans. Pour renouveler / révoquer un pont : arrêter les deux côtés, retirer la configuration Accès avec l'option explicite ci-dessus, créer un nouvel appairage puis importer le nouveau profil sur le pont.

Une sauvegarde de `settings.bin` reste liée au compte Windows / profil DPAPI ; la perte du PC Accès implique un nouvel appairage. Pour revenir à V0.1, remettre les EXE V0.1 des deux côtés, avec le web désactivé ; conserver l'ancienne livraison avant mise à jour.

## Références

- Free IPv4 full-stack : https://assistance.free.fr/articles/1758
- Free redirection de ports : https://assistance.free.fr/articles/1395
- Microsoft RDP : https://learn.microsoft.com/windows-server/remote/remote-desktop-services/remotepc/remote-desktop-allow-access
- Microsoft proxy navigateur : https://learn.microsoft.com/deployedge/edge-learnmore-cmdline-options-proxy-settings
