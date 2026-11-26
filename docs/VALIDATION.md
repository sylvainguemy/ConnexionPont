# Validation

## Vérifié dans l'environnement de développement

- Compilation des deux interfaces Windows avec le compilateur .NET Framework intégré.
- Génération de certificats RSA, empreintes correctes et rejet d'une empreinte différente.
- Export/import du profil chiffré, absence de clé privée relais dans l'export.
- Refus d'un mauvais mot de passe, d'un profil modifié et d'un profil trop volumineux.
- Enregistrement / lecture de la configuration protégée DPAPI.

## Tests réseau automatisés

`scripts/test.ps1` lance le parcours TLS complet avec deux services TCP de test, vérifie l'intégrité binaire, la séparation des routes, les sessions concurrentes, le rejet d'un agent non appairé et d'une session inconnue, la fermeture, puis la reconnexion.

**Résultat : 20 contrôles réussis sur Windows Server 2025, le 26 novembre 2026.** [Exécution GitHub vérifiée](https://github.com/sylvainguemy/ConnexionPont/actions/runs/45773859635), pour les sources du commit `329ee312080bec34b1b780bf2f08591c341b8741`.

Le parcours vérifié comprend l'authentification mutuelle TLS, deux transferts binaires de 1 Mio sur les routes RDP et SSH, quatre flux simultanés distincts, la libération des sessions, le rejet d'une session inconnue et d'un agent non appairé, l'arrêt du relais, la reconnexion automatique et l'utilisation de SSH après reprise.

L'environnement local de développement bloque les connexions TCP, même vers la boucle locale (`WSAEACCES`) ; ces tests réseau ont donc été exécutés sur le runner GitHub Windows. Le workflow ne publie son ZIP que si tous les tests passent.

Les deux interfaces WinForms ont également été construites et rendues localement ; le rendu visuel a été contrôlé. Le test optionnel `scripts/test-ui.ps1` permet de reproduire ce contrôle (il ouvre brièvement les deux fenêtres et écrit les images dans `artifacts`, hors livraison).

## Qualification à effectuer chez l'utilisateur

- Freebox : plage IPv4 / full-stack, redirection, pare-feu, connexion depuis un réseau externe.
- PC client : édition Windows compatible RDP, service, autorisations et compte Windows.
- Serveur SSH : adresse interne réelle, port 22 joignable depuis le PC client, authentification et empreinte du serveur.
- RDP et SSH réels, veille / réveil, réseau du client et éventuel proxy / inspection TLS.
- Installation et désinstallation avec les droits du poste cible ; EXE non signés et politiques locales.

V0.1 : pas d'audit indépendant et pas de validation de production implicite.

## V0.2 — accès web et lancement sans installation

Le parcours de tests couvre aussi SOCKS5 IPv4 / nom DNS, refus des destinations et ports non autorisés, migration des profils V0.1, conflit de ports, conservation des en-têtes HTTP et cookies sur les deux routes web, douze requêtes simultanées et reprise HTTP après reconnexion. Si Edge / Chrome est installé, le navigateur réel est lancé en mode headless avec le même lanceur et les mêmes URL pour vérifier qu'il utilise le tunnel.

Les résultats à jour sont publiés dans le workflow Windows du dépôt et dans son journal de tests. Le ZIP n'est produit par ce workflow que si les contrôles passent. Les tests utilisent des services locaux simulant les applications ; les URL et authentifications réelles du client restent à qualifier sur son réseau.

L'interface et le guide distinguent le poste Accès chez l'utilisateur et le PC de pont chez le client. Le pont dispose d'une procédure par EXE portable sans installateur ni droits administrateur, sous réserve de .NET Framework 4.8 déjà installé et des autorisations réseau existantes. Aucun paramètre du poste client n'est modifié automatiquement.
